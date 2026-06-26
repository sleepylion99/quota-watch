using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers;

public sealed class CodexUsageProvider : IUsageProvider
{
    private readonly ICodexRateLimitClient _client;
    private readonly CodexUsageMode _mode;
    private readonly ICodexResetCreditsClient? _resetCreditsClient;

    public CodexUsageProvider()
        : this(CodexCompositeRateLimitClient.CreateDefault(), CodexUsageMode.Auto, new CodexWhamResetCreditsClient())
    {
    }

    public CodexUsageProvider(CodexUsageMode mode)
        : this(CodexCompositeRateLimitClient.CreateDefault(mode), mode, new CodexWhamResetCreditsClient())
    {
    }

    public CodexUsageProvider(CodexUsageMode mode, string authPath)
        : this(
            CodexCompositeRateLimitClient.CreateDefault(mode, authPath),
            mode,
            new CodexWhamResetCreditsClient(
                new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
                Path.GetFullPath(authPath)))
    {
    }

    internal CodexUsageProvider(
        ICodexRateLimitClient client,
        CodexUsageMode mode = CodexUsageMode.Auto,
        ICodexResetCreditsClient? resetCreditsClient = null)
    {
        _client = client;
        _mode = mode;
        _resetCreditsClient = resetCreditsClient;
    }

    public ProviderDescriptor Descriptor { get; } = new("codex", "ChatGPT Codex", true);

    public async Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rateLimits = await _client.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false);
            var windows = new List<UsageWindow>();
            if (rateLimits.Primary is not null)
            {
                windows.Add(BuildWindow(rateLimits.Primary, "five-hour", "5h limit"));
            }

            if (rateLimits.Secondary is not null)
            {
                windows.Add(BuildWindow(rateLimits.Secondary, "weekly", "Weekly limit"));
            }

            var showDetailed = _mode == CodexUsageMode.ProDetailed
                || (_mode == CodexUsageMode.Auto && rateLimits.RateLimitsByLimitId is { Count: > 0 });
            if (showDetailed)
            {
                windows.AddRange(BuildDetailedWindows(rateLimits));
            }

            if (windows.Count == 0)
            {
                return Failed("Codex app-server returned no rate-limit windows.");
            }

            var snapshot = new UsageSnapshot(
                Descriptor.Id,
                Descriptor.DisplayName,
                DateTimeOffset.Now,
                UsageSource.Agent,
                UsageStatus.Fresh,
                windows,
                AccountKey: rateLimits.AccountKey);

            return await AttachResetCreditsAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ex.Message);
        }
    }

    private static UsageWindow BuildWindow(
        CodexRpcRateLimitWindow window,
        string fallbackId,
        string fallbackLabel)
    {
        var minutes = window.WindowDurationMins;
        // A monthly window is ~28-31 days; Free / Go plans expose only this one.
        var id = minutes switch
        {
            300 => "five-hour",
            10080 => "weekly",
            >= 40000 and <= 45000 => "monthly",
            _ when minutes is not null => $"{minutes}-minute",
            _ => fallbackId
        };
        var label = minutes switch
        {
            300 => "5h limit",
            10080 => "Weekly limit",
            >= 40000 and <= 45000 => "Monthly limit",
            _ when minutes is not null => $"{minutes}-minute limit",
            _ => fallbackLabel
        };

        DateTimeOffset? resetAt = window.ResetsAt is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt.Value);

        // Free / Go plans omit window_duration_mins and expose only a monthly window,
        // so the code above falls back to "five-hour". But a reset more than ~8 days
        // out can only be a monthly window (5h resets within 5h, weekly within 7d) —
        // relabel it accordingly using the reset time as the reliable signal.
        if (id != "monthly"
            && resetAt is { } reset
            && reset - DateTimeOffset.UtcNow > TimeSpan.FromDays(8))
        {
            id = "monthly";
            label = "Monthly limit";
        }

        return new UsageWindow(
            id,
            label,
            UsageCalculations.PercentRemaining(window.UsedPercent),
            resetAt,
            null,
            "high");
    }

    private static IEnumerable<UsageWindow> BuildDetailedWindows(CodexRpcRateLimits rateLimits)
    {
        if (rateLimits.RateLimitsByLimitId is null)
        {
            yield break;
        }

        foreach (var item in rateLimits.RateLimitsByLimitId)
        {
            var bucket = item.Value;
            var name = NormalizeLimitName(item.Key, bucket.LimitName);
            if (bucket.Primary is not null)
            {
                yield return BuildWindow($"{SanitizeId(item.Key)}-primary", $"{name} 5h limit", bucket.Primary);
            }

            if (bucket.Secondary is not null)
            {
                yield return BuildWindow($"{SanitizeId(item.Key)}-secondary", $"{name} weekly limit", bucket.Secondary);
            }

            if (bucket.Credits is not null)
            {
                yield return new UsageWindow(
                    $"{SanitizeId(item.Key)}-credits",
                    $"{name} credits",
                    bucket.Credits.PercentRemaining,
                    null,
                    $"Remaining credits: {bucket.Credits.Balance:g}",
                    "medium");
            }
        }
    }

    private static UsageWindow BuildWindow(string id, string label, CodexRpcRateLimitWindow window)
    {
        DateTimeOffset? resetAt = window.ResetsAt is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt.Value);

        return new UsageWindow(
            id,
            label,
            UsageCalculations.PercentRemaining(window.UsedPercent),
            resetAt,
            null,
            "high");
    }

    private UsageSnapshot Failed(string message)
    {
        return new UsageSnapshot(
            Descriptor.Id,
            Descriptor.DisplayName,
            DateTimeOffset.Now,
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            message);
    }

    private async Task<UsageSnapshot> AttachResetCreditsAsync(
        UsageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (_resetCreditsClient is null)
        {
            return snapshot;
        }

        try
        {
            var credits = await _resetCreditsClient.ReadResetCreditsAsync(cancellationToken).ConfigureAwait(false);
            var summary = BuildResetCreditSummary(credits);
            return summary is null ? snapshot : snapshot with { ResetCredits = summary };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return snapshot;
        }
    }

    internal static ResetCreditSummary? BuildResetCreditSummary(CodexResetCredits? credits)
    {
        if (credits is null)
        {
            return null;
        }

        var available = credits.Credits.Where(entry => entry.IsAvailable).ToList();
        if (available.Count == 0)
        {
            return null;
        }

        var count = credits.AvailableCount > 0 ? credits.AvailableCount : available.Count;

        DateTimeOffset? nearest = available
            .Where(entry => entry.ExpiresAt is not null)
            .Select(entry => entry.ExpiresAt!.Value)
            .OrderBy(value => value)
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();

        return new ResetCreditSummary(count, nearest);
    }

    private static string SanitizeId(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static string NormalizeLimitName(string id, string? name)
    {
        var candidate = string.IsNullOrWhiteSpace(name) ? id : name!;
        return candidate;
    }
}

public enum CodexUsageMode
{
    Auto,
    Basic,
    ProDetailed
}

internal interface ICodexRateLimitClient
{
    Task<CodexRpcRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken);
}

internal sealed class CodexCompositeRateLimitClient(IReadOnlyList<ICodexRateLimitClient> clients) : ICodexRateLimitClient
{
    internal IReadOnlyList<ICodexRateLimitClient> Clients { get; } = clients;

    public static ICodexRateLimitClient CreateDefault()
    {
        return CreateDefault(CodexUsageMode.Auto);
    }

    public static ICodexRateLimitClient CreateDefault(CodexUsageMode mode)
    {
        var appServer = new CodexAppServerRateLimitClient();
        return new CodexCompositeRateLimitClient([new CodexWhamRateLimitClient(), appServer]);
    }

    public static ICodexRateLimitClient CreateDefault(CodexUsageMode mode, string authPath)
    {
        var normalizedAuthPath = Path.GetFullPath(authPath);
        var codexHome = Path.GetDirectoryName(normalizedAuthPath)
            ?? throw new ArgumentException("Codex auth path must have a parent directory.", nameof(authPath));
        var wham = new CodexWhamRateLimitClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
            normalizedAuthPath);
        var appServer = new CodexAppServerRateLimitClient(
            CodexAppServerRateLimitClient.ResolveCodexExecutable(),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(4),
            codexHome);
        return new CodexCompositeRateLimitClient([wham, appServer]);
    }

    public async Task<CodexRpcRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var client in Clients)
        {
            try
            {
                return await client.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("Codex usage clients were not configured.");
    }
}

internal sealed class CodexWhamRateLimitClient : ICodexRateLimitClient
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private readonly HttpClient _httpClient;
    private readonly string _authPath;

    public CodexWhamRateLimitClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, ResolveAuthPath())
    {
    }

    internal CodexWhamRateLimitClient(HttpClient httpClient, string authPath)
    {
        _httpClient = httpClient;
        _authPath = authPath;
    }

    public async Task<CodexRpcRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        var token = ReadAccessToken(_authPath);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Codex OAuth token was not found in auth.json.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Codex OAuth usage failed: HTTP {(int)response.StatusCode}.");
        }

        return ParseWhamUsage(body) with
        {
            AccountKey = AccountKeyHash.FromSecret("codex", token)
        };
    }

    internal static CodexRpcRateLimits ParseWhamUsage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var source = TryGetProperty(root, "rate_limit", "rateLimit") ?? root;
        var additional = new Dictionary<string, CodexRpcRateLimitBucket>(StringComparer.Ordinal);

        if (TryGetProperty(source, "additional_rate_limits", "additionalRateLimits") is { } additionalLimits
            && additionalLimits.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in additionalLimits.EnumerateArray())
            {
                var id = ReadString(item, "limit_id", "limitId", "id") ?? "codex-extra";
                var name = ReadString(item, "title", "name", "label", "limit_name", "limitName") ?? id;
                additional[id] = new CodexRpcRateLimitBucket(
                    id,
                    name,
                    ReadWindow(item, "primary_window", "primaryWindow", "primary"),
                    ReadWindow(item, "secondary_window", "secondaryWindow", "secondary"),
                    ReadCredits(item),
                    ReadString(item, "plan_type", "planType"));
            }
        }

        return new CodexRpcRateLimits(
            ReadWindow(source, "primary_window", "primaryWindow", "primary"),
            ReadWindow(source, "secondary_window", "secondaryWindow", "secondary"),
            ReadString(root, "plan_type", "planType") ?? ReadString(source, "plan_type", "planType"),
            additional.Count == 0 ? null : additional);
    }

    private static CodexRpcRateLimitWindow? ReadWindow(JsonElement source, params string[] names)
    {
        var window = TryGetProperty(source, names);
        if (window is null || window.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CodexRpcRateLimitWindow(
            ReadDouble(window.Value, "used_percent", "usedPercent", "used_percentage", "usedPercentage") ?? 0,
            ReadInt(window.Value, "window_duration_mins", "windowDurationMins", "window_minutes", "windowMinutes"),
            ReadLong(window.Value, "resets_at", "resetsAt", "reset_at", "resetAt"));
    }

    private static CodexRpcCredits? ReadCredits(JsonElement source)
    {
        var credits = TryGetProperty(source, "credits", "credit_balance", "creditBalance");
        if (credits is null)
        {
            return null;
        }

        if (credits.Value.ValueKind == JsonValueKind.Object)
        {
            var balance = ReadDouble(credits.Value, "balance", "remaining", "remaining_credits", "remainingCredits");
            return balance is null ? null : new CodexRpcCredits(balance.Value);
        }

        return TryGetDouble(credits.Value, out var value) ? new CodexRpcCredits(value) : null;
    }

    private static JsonElement? TryGetProperty(JsonElement source, params string[] names)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement source, params string[] names)
    {
        var value = TryGetProperty(source, names);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

    private static int? ReadInt(JsonElement source, params string[] names)
    {
        var value = TryGetProperty(source, names);
        if (value is null)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.Value.ValueKind == JsonValueKind.String && int.TryParse(value.Value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static long? ReadLong(JsonElement source, params string[] names)
    {
        var value = TryGetProperty(source, names);
        if (value is null)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.Value.ValueKind == JsonValueKind.String && long.TryParse(value.Value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(JsonElement source, params string[] names)
    {
        var value = TryGetProperty(source, names);
        return value is null ? null : TryGetDouble(value.Value, out var number) ? number : null;
    }

    private static bool TryGetDouble(JsonElement value, out double result)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return double.TryParse(value.GetString(), out result);
        }

        result = 0;
        return false;
    }

    internal static string? ReadAccessToken(string authPath)
    {
        try
        {
            if (!File.Exists(authPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(authPath));
            var root = document.RootElement;
            if (root.TryGetProperty("tokens", out var tokens)
                && tokens.TryGetProperty("access_token", out var token))
            {
                return token.GetString();
            }

            return root.TryGetProperty("access_token", out var rootToken)
                ? rootToken.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    internal static string ResolveAuthPath()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "auth.json");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "auth.json");
    }
}

internal sealed class CodexAppServerRateLimitClient : ICodexRateLimitClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _codexExecutable;
    private readonly TimeSpan _initializeTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly string? _codexHome;

    public CodexAppServerRateLimitClient()
        : this(ResolveCodexExecutable(), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(4), null)
    {
    }

    public CodexAppServerRateLimitClient(
        string codexExecutable,
        TimeSpan initializeTimeout,
        TimeSpan requestTimeout,
        string? codexHome = null)
    {
        _codexExecutable = codexExecutable;
        _initializeTimeout = initializeTimeout;
        _requestTimeout = requestTimeout;
        _codexHome = codexHome;
    }

    public async Task<CodexRpcRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        using var process = StartProcess();
        try
        {
            await RequestAsync<InitializeResult>(
                process,
                1,
                "initialize",
                new { clientInfo = new { name = "ai-limit", version = "0.1" } },
                _initializeTimeout,
                cancellationToken).ConfigureAwait(false);
            await SendNotificationAsync(process, "initialized", cancellationToken).ConfigureAwait(false);

            var response = await RequestAsync<RateLimitsResponse>(
                process,
                2,
                "account/rateLimits/read",
                null,
                _requestTimeout,
                cancellationToken).ConfigureAwait(false);
            return response.RateLimits;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private Process StartProcess()
    {
        var startInfo = CreateAppServerStartInfo(_codexExecutable, _codexHome);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Codex app-server.");
    }

    internal static ProcessStartInfo CreateAppServerStartInfo(string codexExecutable)
    {
        return CreateAppServerStartInfo(codexExecutable, null);
    }

    internal static ProcessStartInfo CreateAppServerStartInfo(string codexExecutable, string? codexHome)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = codexExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add("read-only");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("untrusted");
        startInfo.ArgumentList.Add("app-server");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            startInfo.Environment["CODEX_HOME"] = codexHome;
        }

        return startInfo;
    }

    private static async Task<T> RequestAsync<T>(
        Process process,
        int id,
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new JsonRpcRequest(id, method, parameters ?? new { }), JsonOptions);
        await process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException(await ReadAppServerErrorAsync(process).ConfigureAwait(false));
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
            {
                continue;
            }

            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.GetProperty("message").GetString());
            }

            return root.GetProperty("result").Deserialize<T>(JsonOptions)
                ?? throw new InvalidOperationException($"Codex app-server returned an empty `{method}` result.");
        }
    }

    private static async Task SendNotificationAsync(
        Process process,
        string method,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new JsonRpcNotification(method, new { }), JsonOptions);
        await process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadAppServerErrorAsync(Process process)
    {
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(error)
            ? "Codex app-server closed stdout."
            : error.Trim();
    }

    internal static string ResolveCodexExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured) && !IsUnsafeWindowsCodexLauncher(configured))
        {
            return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            var npmCodex = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm",
                "codex.cmd");
            if (File.Exists(npmCodex))
            {
                return npmCodex;
            }

            var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var fileName in new[] { "codex.cmd", "codex.exe" })
            {
                foreach (var pathEntry in pathEntries)
                {
                    var candidate = Path.Combine(pathEntry, fileName);
                    if (File.Exists(candidate) && !IsUnsafeWindowsCodexLauncher(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return "codex.cmd";
        }

        return "codex";
    }

    private static bool IsUnsafeWindowsCodexLauncher(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Contains($"{Path.DirectorySeparatorChar}.codex{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}arg0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record JsonRpcRequest(int Id, string Method, object Params);

    private sealed record JsonRpcNotification(string Method, object Params);

    private sealed record InitializeResult;

    private sealed record RateLimitsResponse(CodexRpcRateLimits RateLimits);
}

internal sealed record CodexRpcRateLimits(
    CodexRpcRateLimitWindow? Primary,
    CodexRpcRateLimitWindow? Secondary,
    string? PlanType,
    IReadOnlyDictionary<string, CodexRpcRateLimitBucket>? RateLimitsByLimitId = null,
    string? AccountKey = null);

internal sealed record CodexRpcRateLimitWindow(
    double UsedPercent,
    int? WindowDurationMins,
    long? ResetsAt);

internal sealed record CodexRpcRateLimitBucket(
    string? LimitId,
    string? LimitName,
    CodexRpcRateLimitWindow? Primary,
    CodexRpcRateLimitWindow? Secondary,
    CodexRpcCredits? Credits,
    string? PlanType);

internal sealed record CodexRpcCredits(
    double Balance)
{
    public int PercentRemaining => Balance > 0 ? 100 : 0;
}
