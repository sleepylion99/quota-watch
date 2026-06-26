using System.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers;

public sealed class AntigravityUsageProvider : IUsageProvider
{
    private readonly IAntigravityUsageClient _client;
    private readonly Func<bool> _hasOAuthClientValues;
    private readonly Func<AntigravityOAuthClientOrigin> _resolveOAuthClientOrigin;

    public AntigravityUsageProvider()
        : this(new AntigravityOAuthUsageClient())
    {
    }

    internal AntigravityUsageProvider(
        IAntigravityUsageClient client,
        Func<bool>? hasOAuthClientValues = null,
        Func<AntigravityOAuthClientOrigin>? resolveOAuthClientOrigin = null)
    {
        _client = client;
        _hasOAuthClientValues = hasOAuthClientValues ?? HasConfiguredOAuthClientValues;
        _resolveOAuthClientOrigin = resolveOAuthClientOrigin
            ?? AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin;
    }

    private static string OAuthClientOriginToken(AntigravityOAuthClientOrigin origin) => origin switch
    {
        AntigravityOAuthClientOrigin.Environment => "environment",
        AntigravityOAuthClientOrigin.IdeCredentialFile => "ide",
        AntigravityOAuthClientOrigin.UserSavedSettings => "user-saved",
        AntigravityOAuthClientOrigin.BundledDefault => "bundled",
        _ => "none"
    };

    public ProviderDescriptor Descriptor { get; } = new("gemini-pro", "Google Antigravity", true);

    public static AntigravityOAuthClientOrigin GetActiveOAuthClientOrigin()
        => AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin();

    /// <summary>
    /// Builds an <see cref="AntigravityUsageProvider"/> whose underlying OAuth
    /// client consults <paramref name="credentialResolver"/> for credentials
    /// each refresh. Used by the dashboard composition root so the tile honours
    /// the user's selected account from the Accounts window; the resolver falls
    /// back to the live keychain when no account is explicitly selected.
    /// </summary>
    internal static AntigravityUsageProvider CreateWithCredentialResolver(
        Func<AntigravityOAuthCredentials?> credentialResolver,
        HttpClient httpClient,
        Func<bool> allowLocalDetectionResolver)
    {
        var client = new AntigravityOAuthUsageClient(
            httpClient,
            allowLocalDetectionResolver,
            credentialLoader: credentialResolver);
        return new AntigravityUsageProvider(client);
    }

    public async Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var usage = await _client.ReadUsageAsync(cancellationToken).ConfigureAwait(false);

            var windows = BuildWindows(usage.Buckets, usage.Channel).ToList();
            if (windows.Count == 0)
            {
                return Failed(usage.Message ?? "Google Antigravity quota API returned no quota buckets.");
            }

            return new UsageSnapshot(
                Descriptor.Id,
                Descriptor.DisplayName,
                DateTimeOffset.Now,
                UsageSource.Agent,
                UsageStatus.Fresh,
                windows,
                AccountKey: usage.AccountKey,
                SourceChannel: usage.Channel,
                CloudFailureSummary: usage.CloudFailure,
                IdeFailureSummary: usage.IdeFailure,
                OAuthClientOrigin: OAuthClientOriginToken(_resolveOAuthClientOrigin()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ex.Message);
        }
    }

    private static IEnumerable<UsageWindow> BuildWindows(IReadOnlyList<AntigravityQuotaBucket> buckets, string? channel)
    {
        var tracked = buckets.Where(bucket => AntigravityQuotaParser.IsTrackedModel(bucket.ModelId));

        // Both the cloud fetchAvailableModels response and the IDE fallback can list base models the
        // Antigravity model picker does not expose (e.g. "Gemini 3 Flash", "Gemini 3.1 Flash Lite",
        // "Gemini 2.5 Pro"). The picker only surfaces models that carry a reasoning-effort or thinking
        // variant (Medium/High/Low/Thinking), so on a known data source keep only those to match the
        // set the user actually uses. Synthetic reads with no channel are left unfiltered.
        if (channel is "cloud" or "ide-fallback")
        {
            tracked = tracked.Where(bucket => HasReasoningVariant(NormalizeModelLabel(bucket.ModelId)));
        }

        // The cloud response can list several model-map keys that resolve to the same display label
        // (e.g. four "Gemini 3.1 Flash Lite", two "Gemini 3.1 Pro (High)"). Collapse buckets that
        // share a label into a single window, keeping the most-used reading, so identical rows don't
        // appear. Distinct labels — including tier variants such as Medium/High/Low — slugify
        // differently and are preserved.
        return tracked
            .GroupBy(bucket => Slugify(NormalizeModelLabel(bucket.ModelId)))
            .Select(group => group.OrderBy(bucket => bucket.PercentRemaining).First())
            .OrderBy(bucket => AntigravityModelSortKey(bucket.ModelId), StringComparer.OrdinalIgnoreCase)
            .Select(bucket =>
            {
                var label = NormalizeModelLabel(bucket.ModelId);
                return new UsageWindow(
                    $"antigravity-{Slugify(label)}",
                    label,
                    100 - bucket.PercentRemaining,
                    bucket.ResetAt,
                    null,
                    "high",
                    IsUsedPercent: true);
            });
    }

    private static string NormalizeModelLabel(string? modelId)
        => string.IsNullOrWhiteSpace(modelId) ? "Unknown model" : modelId.Trim();

    private static bool HasReasoningVariant(string label)
    {
        // A reasoning-effort tier (Medium/High/Low) or a Thinking marker — present in both the cloud
        // display name ("Gemini 3.5 Flash (Medium)") and the bare model id ("gemini-3.5-flash-medium").
        // Mirrors the tier detection in AntigravityModelSortKey.
        var normalized = label.ToLowerInvariant();
        return normalized.Contains("medium")
            || normalized.Contains("high")
            || normalized.Contains("low")
            || normalized.Contains("thinking");
    }

    private static string AntigravityModelSortKey(string label)
    {
        var normalized = label.ToLowerInvariant();
        var family = normalized switch
        {
            var value when value.Contains("gemini") && value.Contains("flash") => "0",
            var value when value.Contains("gemini") && value.Contains("pro") => "1",
            var value when value.Contains("claude") && value.Contains("sonnet") => "2",
            var value when value.Contains("claude") && value.Contains("opus") => "3",
            var value when value.Contains("gpt") || value.Contains("oss") => "4",
            _ => "9"
        };
        var tier = normalized switch
        {
            var value when value.Contains("medium") => "0",
            var value when value.Contains("high") => "1",
            var value when value.Contains("low") => "2",
            _ => "9"
        };

        return $"{family}:{tier}:{normalized}";
    }

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "model" : slug;
    }

    private static readonly Regex CloudTagPattern =
        new(@"\s*\[cloud=([^\]]*)\]", RegexOptions.CultureInvariant);
    private static readonly Regex IdeTagPattern =
        new(@"\s*\[ide=([^\]]*)\]", RegexOptions.CultureInvariant);

    private static (string Cleaned, string? Cloud, string? Ide) ExtractFailureTags(string message)
    {
        var cloudMatch = CloudTagPattern.Match(message);
        var ideMatch = IdeTagPattern.Match(message);
        var cleaned = IdeTagPattern.Replace(CloudTagPattern.Replace(message, string.Empty), string.Empty).TrimEnd();
        return (
            cleaned,
            cloudMatch.Success ? cloudMatch.Groups[1].Value.Trim() : null,
            ideMatch.Success ? ideMatch.Groups[1].Value.Trim() : null);
    }

    private UsageSnapshot Failed(string message)
    {
        var tags = ExtractFailureTags(message);
        var oauthClientHint = _hasOAuthClientValues()
            ? string.Empty
            : "Google Antigravity OAuth client values were not found. ";
        var guidance =
            "Antigravity Cloud setup: sign in to Antigravity with your Google account. " +
            "If Cloud is unavailable, keep Antigravity IDE running so Quota Watch can try the local IDE fallback. " +
            oauthClientHint +
            "To refresh expired tokens without the IDE, save your own OAuth client values in Settings > Antigravity OAuth or set ANTIGRAVITY_OAUTH_CLIENT_ID and ANTIGRAVITY_OAUTH_CLIENT_SECRET explicitly. " +
            $"Details: {tags.Cleaned}";
        return new UsageSnapshot(
            Descriptor.Id,
            Descriptor.DisplayName,
            DateTimeOffset.Now,
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            guidance,
            SourceChannel: null,
            CloudFailureSummary: tags.Cloud,
            IdeFailureSummary: tags.Ide,
            OAuthClientOrigin: OAuthClientOriginToken(_resolveOAuthClientOrigin()));
    }

    private static bool HasConfiguredOAuthClientValues()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var activeClient = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientConfig();
        if (!string.IsNullOrWhiteSpace(activeClient.ClientId)
            && !string.IsNullOrWhiteSpace(activeClient.ClientSecret))
        {
            return true;
        }

        var credentials = AntigravityOAuthCredentialStore.Load();
        return credentials is not null
            && !string.IsNullOrWhiteSpace(AntigravityOAuthCredentialStore.ResolveOAuthClientId(credentials))
            && !string.IsNullOrWhiteSpace(AntigravityOAuthCredentialStore.TryResolveOAuthClientSecret(credentials));
    }

}

internal interface IAntigravityUsageClient
{
    Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken);
}

internal sealed class AntigravityOAuthUsageClient : IAntigravityUsageClient
{
    private static readonly Uri[] QuotaApiEndpoints =
    [
        new("https://daily-cloudcode-pa.sandbox.googleapis.com/v1internal:fetchAvailableModels"),
        new("https://daily-cloudcode-pa.googleapis.com/v1internal:fetchAvailableModels"),
        new("https://cloudcode-pa.googleapis.com/v1internal:fetchAvailableModels")
    ];

    internal const string LocalRequestBody =
        "{\"metadata\":{\"ideName\":\"Antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\",\"ideVersion\":\"1.0.0\"}}";
    internal const string CloudLoadCodeAssistBody =
        "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\"}}";
    internal const string CloudUserAgent = "antigravity/windows/amd64";
    private readonly HttpClient _httpClient;
    private readonly Func<bool> _allowLocalDetectionResolver;
    private readonly Func<AntigravityOAuthCredentials?> _credentialLoader;
    // Single nullable tuple so reader and writer observe a consistent
    // (token, expiry) snapshot. Two independent fields permitted a torn
    // read where a fresh token could pair with a stale/null expiry.
    private (string Token, DateTimeOffset ExpiresAt)? _cachedAccessToken;

    public AntigravityOAuthUsageClient()
        : this(
            new HttpClient()
            {
                Timeout = TimeSpan.FromSeconds(15)
            },
            allowLocalDetection: true)
    {
    }

    internal AntigravityOAuthUsageClient(HttpClient httpClient)
        : this(httpClient, allowLocalDetection: true)
    {
    }

    internal AntigravityOAuthUsageClient(HttpClient httpClient, Func<AntigravityOAuthCredentials?> credentialLoader)
        : this(httpClient, allowLocalDetection: true, credentialLoader: credentialLoader)
    {
    }

    internal AntigravityOAuthUsageClient(HttpClient httpClient, bool allowLocalDetection, Func<AntigravityOAuthCredentials?>? credentialLoader = null)
        : this(httpClient, () => allowLocalDetection, credentialLoader)
    {
    }

    internal AntigravityOAuthUsageClient(
        HttpClient httpClient,
        Func<bool> allowLocalDetectionResolver,
        Func<AntigravityOAuthCredentials?>? credentialLoader = null)
    {
        _httpClient = httpClient;
        _allowLocalDetectionResolver = allowLocalDetectionResolver
            ?? throw new ArgumentNullException(nameof(allowLocalDetectionResolver));
        _credentialLoader = credentialLoader ?? AntigravityOAuthCredentialStore.Load;
    }

    public async Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken)
    {
        Exception? cloudFailure = null;
        string? ideFallbackDetail = null;
        var allowLocalDetection = _allowLocalDetectionResolver();

        if (allowLocalDetection)
        {
            var localUsage = await TryReadIdeUsageAsync(cancellationToken).ConfigureAwait(false);
            if (localUsage.Buckets.Count > 0)
            {
                return AntigravityUsageReadResult.Fresh(
                    localUsage.Buckets,
                    localUsage.AccountKey,
                    channel: "ide-fallback",
                    cloudFailure: null);
            }

            ideFallbackDetail = localUsage.Message;
        }

        var cloudUsage = await TryReadCloudUsageAsync(cancellationToken).ConfigureAwait(false);
        if (cloudUsage.Buckets.Count > 0)
        {
            return cloudUsage;
        }

        var cloudSummary = SummarizeCloudFailure(cloudFailure) ?? "unavailable";

        if (allowLocalDetection && !AntigravityInstallation.IsProbablyInstalled())
        {
            throw new InvalidOperationException(
                "Antigravity installation was not found. Install Antigravity, then sign in with your Google account."
                + $" [cloud={cloudSummary}] [ide=installation not detected]");
        }

        var ideSummary = SummarizeIdeFailure(ideFallbackDetail) ?? "unavailable";
        var cloudDetail = cloudFailure is null ? string.Empty : $" Last cloud error: {cloudFailure.Message}";
        var ideDetail = string.IsNullOrWhiteSpace(ideFallbackDetail) ? string.Empty : $" {ideFallbackDetail}";
        throw new InvalidOperationException(
            $"Antigravity quota was not available. Sign in to Antigravity with a Google account, or start Antigravity IDE as a fallback.{ideDetail}{cloudDetail}"
            + $" [cloud={cloudSummary}] [ide={ideSummary}]");

        async Task<AntigravityUsageReadResult> TryReadCloudUsageAsync(CancellationToken token)
        {
            try
            {
                return await ReadCloudUsageAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            {
                cloudFailure = new TimeoutException("Cloud request timed out.", ex);
                WriteAntigravityDiagnostic("Warning", "Cloud quota read timed out; trying IDE fallback.");
                return AntigravityUsageReadResult.Fresh([]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                cloudFailure = ex;
                WriteAntigravityDiagnostic("Warning", $"Cloud quota read failed. {ex.GetType().Name}: {ex.Message}");
                return AntigravityUsageReadResult.Fresh([]);
            }
        }
    }

    private async Task<AntigravityUsageReadResult> ReadCloudUsageAsync(CancellationToken cancellationToken)
    {
        var accessToken = await ResolveAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Google Antigravity OAuth credentials were not found. Sign in to Antigravity with a Google account.");
        }

        var response = await SendCloudCodeRequestAsync(
            "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist",
            accessToken,
            CloudLoadCodeAssistBody,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            accessToken = await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            response = await SendCloudCodeRequestAsync(
                "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist",
                accessToken,
                CloudLoadCodeAssistBody,
                cancellationToken).ConfigureAwait(false);
        }

        string loadCodeAssistBody = string.Empty;
        using (response)
        {
            loadCodeAssistBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                WriteAntigravityDiagnostic(
                    "Warning",
                    $"Google Antigravity loadCodeAssist failed: HTTP {(int)response.StatusCode}; trying fetchAvailableModels without project.");
                loadCodeAssistBody = string.Empty;
            }
        }

        var projectId = AntigravityQuotaParser.ExtractProjectId(loadCodeAssistBody);
        var modelsBody = await FetchAvailableModelsAsync(accessToken, projectId, cancellationToken).ConfigureAwait(false);

        return AntigravityUsageReadResult.Fresh(
            AntigravityQuotaParser.ParseQuotaBuckets(modelsBody),
            AntigravityQuotaParser.ExtractAccountKey(loadCodeAssistBody) ?? AntigravityQuotaParser.ExtractAccountKey(modelsBody),
            channel: "cloud");
    }

    private async Task<string> FetchAvailableModelsAsync(string accessToken, string? projectId, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var endpointIndex = 0; endpointIndex < QuotaApiEndpoints.Length; endpointIndex++)
        {
            var endpoint = QuotaApiEndpoints[endpointIndex];
            var hasNextEndpoint = endpointIndex + 1 < QuotaApiEndpoints.Length;
            var fetchBody = BuildQuotaRequestBody(projectId);
            var retriedWithoutProject = false;

            while (true)
            {
                try
                {
                    using var response = await SendCloudCodeRequestAsync(
                        endpoint.ToString(),
                        accessToken,
                        fetchBody,
                        cancellationToken).ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return body;
                    }

                    if (response.StatusCode == HttpStatusCode.Forbidden
                        && !retriedWithoutProject
                        && fetchBody.Contains("project", StringComparison.Ordinal))
                    {
                        fetchBody = "{}";
                        retriedWithoutProject = true;
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new InvalidOperationException("Google Antigravity fetchAvailableModels failed: HTTP 401.");
                    }

                    var status = (int)response.StatusCode;
                    var error = new InvalidOperationException($"Google Antigravity fetchAvailableModels failed: HTTP {status}.");
                    lastError = error;
                    if (hasNextEndpoint && (status == 429 || status >= 500))
                    {
                        WriteAntigravityDiagnostic("Warning", $"Quota endpoint failed with HTTP {status}; trying fallback endpoint.");
                        break;
                    }

                    _ = body;
                    throw error;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    if (hasNextEndpoint && !IsPermanentQuota4xx(ex.Message))
                    {
                        WriteAntigravityDiagnostic("Warning", $"Quota endpoint request failed; trying fallback endpoint. {ex.GetType().Name}: {ex.Message}");
                        break;
                    }

                    throw;
                }
            }
        }

        throw lastError ?? new InvalidOperationException("Google Antigravity fetchAvailableModels failed.");
    }

    private static string BuildQuotaRequestBody(string? projectId)
    {
        return string.IsNullOrWhiteSpace(projectId)
            ? "{}"
            : JsonSerializer.Serialize(new Dictionary<string, string> { ["project"] = projectId });
    }

    private static bool IsPermanentQuota4xx(string message)
    {
        return Regex.IsMatch(message, @"HTTP 4\d\d\b", RegexOptions.CultureInvariant)
            && !message.Contains("HTTP 429", StringComparison.Ordinal);
    }

    internal static string? ResolveAccessToken()
    {
        return AntigravityOAuthCredentialStore.ResolveAccessToken();
    }

    internal async Task<string?> ResolveAccessTokenAsync(CancellationToken cancellationToken)
    {
        var envToken = AntigravityOAuthCredentialStore.ResolveEnvironmentAccessToken();
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            return envToken;
        }

        if (_cachedAccessToken is { Token: var token, ExpiresAt: var expiry }
            && expiry > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return token;
        }

        var credentials = _credentialLoader();
        if (credentials is null)
        {
            return null;
        }

        // Keychain rotation: if Antigravity wrote a different access token after
        // our last refresh (e.g. the user re-authenticated), drop the cache so we
        // surface the keychain's value instead of a stale refreshed token.
        // This is partial invalidation by design — the cache fast-path above will
        // still return the stale token until its recorded expiry passes. Polling
        // the keychain on every cache hit would negate the cache; this branch
        // catches rotation on the next post-expiry refresh.
        if (_cachedAccessToken is { Token: var cachedToken }
            && !string.Equals(cachedToken, credentials.AccessToken, StringComparison.Ordinal))
        {
            _cachedAccessToken = null;
        }

        return credentials.ShouldRefresh(DateTimeOffset.UtcNow)
            ? await RefreshAccessTokenAsync(credentials, cancellationToken).ConfigureAwait(false)
            : credentials.AccessToken;
    }

    private async Task<AntigravityUsageReadResult> TryReadIdeUsageAsync(CancellationToken cancellationToken)
    {
        var endpoint = AntigravityLocalEndpoint.TryDiscover();
        if (endpoint is null)
        {
            WriteAntigravityDiagnostic("Warning", "IDE endpoint discovery returned no candidates.");
            return AntigravityUsageReadResult.Fresh(
                [],
                message: "IDE fallback was not available because no endpoint was discovered.");
        }

        var attempts = new List<string>();
        foreach (var baseUrl in endpoint.BaseUrls)
        {
            try
            {
                foreach (var path in AntigravityLocalEndpoint.RequestPaths)
                {
                    var csrfToken = endpoint.ShouldSendCsrfToken(baseUrl) ? endpoint.CsrfToken : null;
                    using var response = await SendLocalRequestAsync(baseUrl, csrfToken, path, cancellationToken)
                        .ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        attempts.Add($"{baseUrl}{path} HTTP {(int)response.StatusCode}");
                        continue;
                    }

                    var buckets = AntigravityQuotaParser.ParseQuotaBuckets(body);
                    if (buckets.Count > 0)
                    {
                        var accountKey = AntigravityQuotaParser.ExtractAccountKey(body);
                        WriteAntigravityDiagnostic("Info", $"IDE quota read succeeded. endpoint={baseUrl}, path={path}, buckets={buckets.Count}, accountKey={(string.IsNullOrWhiteSpace(accountKey) ? "missing" : "present")}");
                        return AntigravityUsageReadResult.Fresh(buckets, accountKey);
                    }

                    attempts.Add($"{baseUrl}{path} HTTP {(int)response.StatusCode}, no quota buckets, bytes={body.Length}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                attempts.Add($"{baseUrl} {ex.GetType().Name}: {ex.Message}");
                // Try the next discovered Antigravity port.
            }
        }

        WriteAntigravityDiagnostic(
            "Warning",
            $"IDE quota read exhausted {endpoint.BaseUrls.Count} endpoint(s). csrf={(string.IsNullOrWhiteSpace(endpoint.CsrfToken) ? "missing" : "present")}. attempts={string.Join(" | ", attempts.Take(12))}");
        return AntigravityUsageReadResult.Fresh(
            [],
            message: "IDE fallback was attempted but returned no quota buckets.");
    }

    private static string? SummarizeCloudFailure(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        if (exception is TimeoutException)
        {
            return "timeout";
        }

        var message = exception.Message ?? exception.GetType().Name;
        if (message.Contains("no OAuth client secret is available", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OAuth client secret was not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OAuth client values were not found", StringComparison.OrdinalIgnoreCase))
        {
            return "oauth client secret missing";
        }

        if (message.Contains("OAuth credentials were not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("refresh token was not found", StringComparison.OrdinalIgnoreCase))
        {
            return "sign in required";
        }

        var httpMatch = Regex.Match(message, @"HTTP\s+(\d{3})", RegexOptions.CultureInvariant);
        return httpMatch.Success ? $"HTTP {httpMatch.Groups[1].Value}" : message.Trim();
    }

    private static string? SummarizeIdeFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        if (message.Contains("no endpoint was discovered", StringComparison.OrdinalIgnoreCase))
        {
            return "no endpoint discovered";
        }

        if (message.Contains("returned no quota buckets", StringComparison.OrdinalIgnoreCase))
        {
            return "endpoint reachable, no quota";
        }

        return message.Trim();
    }

    private static void WriteAntigravityDiagnostic(string level, string message)
    {
        if (!IsDiagnosticLoggingEnabled())
        {
            return;
        }

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AiLimit");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "dashboard-debug.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{level}] [Antigravity] {DiagnosticSanitizer.Redact(message)}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect usage refresh.
        }
    }

    private static bool IsDiagnosticLoggingEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AILIMIT_DEBUG_LOG");
        return value is "1" || value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task<HttpResponseMessage> SendLocalRequestAsync(
        string baseUrl,
        string? csrfToken,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}{path}");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("Connect-Protocol-Version", "1");
        if (!string.IsNullOrWhiteSpace(csrfToken))
        {
            request.Headers.Add("X-Codeium-Csrf-Token", csrfToken);
        }

        request.Content = new StringContent(
            LocalRequestBody,
            Encoding.UTF8,
            "application/json");
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCloudCodeRequestAsync(
        string url,
        string accessToken,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("User-Agent", CloudUserAgent);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        var credentials = AntigravityOAuthCredentialStore.Load()
            ?? throw new InvalidOperationException("Google Antigravity OAuth credentials were not found. Run Antigravity login first.");
        return RefreshAccessTokenAsync(credentials, cancellationToken);
    }

    private async Task<string> RefreshAccessTokenAsync(AntigravityOAuthCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            throw new InvalidOperationException("Google Antigravity refresh token was not found. Run Antigravity login again.");
        }

        // Honour client values supplied directly on the credentials record (the
        // account-switcher path threads its own resolver through here). Fall
        // back to the shared env→user-saved→bundle resolver only when the
        // caller did not pre-populate them.
        var clientId = !string.IsNullOrWhiteSpace(credentials.ClientId)
            ? credentials.ClientId
            : AntigravityOAuthCredentialStore.ResolveOAuthClientId(credentials);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Google Antigravity OAuth client ID was not found. Save your own OAuth client values in Settings > Antigravity OAuth or set ANTIGRAVITY_OAUTH_CLIENT_ID explicitly.");
        }

        var clientSecret = !string.IsNullOrWhiteSpace(credentials.ClientSecret)
            ? credentials.ClientSecret
            : AntigravityOAuthCredentialStore.TryResolveOAuthClientSecret(credentials);

        var clientConfig = new AntigravityOAuthClientConfig(clientId, clientSecret);
        var (result, failureStatusCode) = await AntigravityOAuthCredentialStore.RefreshAccessTokenAsync(
            credentials.RefreshToken!,
            clientConfig,
            _httpClient,
            captureFailureStatus: true,
            cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            if (failureStatusCode is { } status)
            {
                if (string.IsNullOrWhiteSpace(clientSecret) && status == HttpStatusCode.BadRequest)
                {
                    throw new InvalidOperationException(
                        "Google Antigravity token refresh failed because no OAuth client secret is available. " +
                        "Save your own OAuth client values in Settings > Antigravity OAuth or set ANTIGRAVITY_OAUTH_CLIENT_ID and ANTIGRAVITY_OAUTH_CLIENT_SECRET to refresh expired tokens without the IDE.");
                }

                throw new InvalidOperationException($"Google Antigravity token refresh failed: HTTP {(int)status}.");
            }

            throw new InvalidOperationException("Google Antigravity token refresh response was missing an access token.");
        }

        var refreshed = result.Value;
        _cachedAccessToken = (refreshed.AccessToken, DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresInSeconds));
        return refreshed.AccessToken;
    }

}

internal enum AntigravityUsageReadStatus
{
    Fresh
}

internal sealed record AntigravityUsageReadResult(
    AntigravityUsageReadStatus Status,
    IReadOnlyList<AntigravityQuotaBucket> Buckets,
    string ConnectionLabel,
    string? Message,
    string? AccountKey = null,
    string? Channel = null,
    string? CloudFailure = null,
    string? IdeFailure = null)
{
    public static AntigravityUsageReadResult Fresh(
        IReadOnlyList<AntigravityQuotaBucket> buckets,
        string? accountKey = null,
        string? message = null,
        string? channel = null,
        string? cloudFailure = null,
        string? ideFailure = null)
    {
        return new AntigravityUsageReadResult(
            AntigravityUsageReadStatus.Fresh,
            buckets,
            string.Empty,
            message,
            accountKey,
            channel,
            cloudFailure,
            ideFailure);
    }
}

internal sealed record AntigravityQuotaBucket(
    string ModelId,
    int PercentRemaining,
    DateTimeOffset? ResetAt);
