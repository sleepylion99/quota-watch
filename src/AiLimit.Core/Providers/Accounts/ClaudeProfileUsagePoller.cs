using System.Text.Json;
using System.Text.Json.Nodes;
using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// Polls one Claude Code profile in place. Expired or rejected access tokens are refreshed and
/// written back only to that profile before the usage request is retried.
/// </summary>
public sealed class ClaudeProfileUsagePoller
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _now;

    public ClaudeProfileUsagePoller(HttpClient httpClient, Func<DateTimeOffset>? now = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AccountSnapshot> PollAsync(string credentialsPath, CancellationToken cancellationToken)
    {
        var (usage, plan) = await PollCoreAsync(credentialsPath, cancellationToken).ConfigureAwait(false);
        if (usage.Status != UsageStatus.Fresh)
        {
            return AccountSnapshot.Failure(usage.LastError ?? "Claude usage poll failed.");
        }

        var buckets = usage.Windows
            .Select(window => new QuotaBucket(
                window.Label,
                window.IsUsedPercent
                    ? 100 - Math.Clamp(window.PercentRemaining, 0, 100)
                    : Math.Clamp(window.PercentRemaining, 0, 100),
                window.ResetAt))
            .ToList()
            .AsReadOnly();
        return AccountSnapshot.Success(buckets, plan);
    }

    /// <summary>
    /// Polls one profile and returns the full usage snapshot (windows + AccountKey) for limit-warning
    /// evaluation. DisplayName stays the provider default; the account label is stamped by the caller.
    /// </summary>
    /// <remarks>
    /// A poll failure (token refresh or HTTP error) is surfaced as a snapshot with
    /// <see cref="UsageStatus.Failed"/> and no windows rather than an exception, so callers must check
    /// <see cref="UsageSnapshot.Status"/> before trusting the windows.
    /// </remarks>
    public async Task<UsageSnapshot> PollUsageAsync(string credentialsPath, CancellationToken cancellationToken)
    {
        var (usage, _) = await PollCoreAsync(credentialsPath, cancellationToken).ConfigureAwait(false);
        return usage;
    }

    private async Task<(UsageSnapshot Usage, AccountPlan Plan)> PollCoreAsync(
        string credentialsPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var credentials = await ReadCredentialsAsync(credentialsPath, cancellationToken).ConfigureAwait(false);
            var accessToken = credentials.AccessToken;
            var refreshed = false;

            if (credentials.ExpiresAtUnixMs is long expiresAt
                && expiresAt <= _now().Add(ExpirySkew).ToUnixTimeMilliseconds())
            {
                var refresh = await RefreshAndPersistAsync(credentialsPath, credentials, cancellationToken)
                    .ConfigureAwait(false);
                if (!refresh.Success)
                {
                    return (FailedUsage(refresh.ErrorMessage!), AccountPlan.Unknown);
                }

                accessToken = refresh.AccessToken!;
                refreshed = true;
            }

            var usage = await ReadUsageAsync(accessToken, cancellationToken).ConfigureAwait(false);
            if (IsUnauthorized(usage) && !refreshed)
            {
                var refresh = await RefreshAndPersistAsync(credentialsPath, credentials, cancellationToken)
                    .ConfigureAwait(false);
                if (!refresh.Success)
                {
                    return (FailedUsage(refresh.ErrorMessage!), AccountPlan.Unknown);
                }

                accessToken = refresh.AccessToken!;
                usage = await ReadUsageAsync(accessToken, cancellationToken).ConfigureAwait(false);
            }

            if (usage.Status != UsageStatus.Fresh)
            {
                return (usage, AccountPlan.Unknown);
            }

            var plan = ClaudeProfileScanner.MapPlan(credentials.SubscriptionType);
            ClaudeAccount? account = null;
            try
            {
                account = await new ClaudeOAuthCredentialStore(_httpClient)
                    .FetchAccountAsync(accessToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }

            if (account is not null)
            {
                var profilePath = Path.GetDirectoryName(credentialsPath);
                if (!string.IsNullOrWhiteSpace(profilePath))
                {
                    ClaudeAccountCache.Write(profilePath, account.Email);
                }

                if (account.HasMax) { plan = AccountPlan.Max; }
                else if (account.HasPro) { plan = AccountPlan.Pro; }
            }

            return (usage, plan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return (FailedUsage(ex.Message), AccountPlan.Unknown);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or InvalidOperationException
                                   or HttpRequestException)
        {
            return (FailedUsage(ex.Message), AccountPlan.Unknown);
        }
    }

    private UsageSnapshot FailedUsage(string error)
        => new("claude", "Claude Code", _now(), UsageSource.Agent, UsageStatus.Failed, [], LastError: error);

    private async Task<UsageSnapshot> ReadUsageAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = new ClaudeOAuthUsageClient(_httpClient, () => accessToken);
        return await new ClaudeUsageProvider("claude", "Claude Code", client)
            .RefreshAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RefreshResult> RefreshAndPersistAsync(
        string credentialsPath,
        ProfileCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            return RefreshResult.Failure("Claude access token expired, but no refresh token was found.");
        }

        var token = await new ClaudeOAuthCredentialStore(_httpClient)
            .RefreshAccessTokenAsync(credentials.RefreshToken, cancellationToken)
            .ConfigureAwait(false);
        if (!token.Success || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return RefreshResult.Failure($"Claude token refresh failed: {token.ErrorMessage ?? "unknown error"}");
        }

        credentials.OAuth["accessToken"] = token.AccessToken;
        credentials.OAuth["refreshToken"] = string.IsNullOrWhiteSpace(token.RefreshToken)
            ? credentials.RefreshToken
            : token.RefreshToken;
        if (token.ExpiresAtUnixMs is long expiresAt)
        {
            credentials.OAuth["expiresAt"] = expiresAt;
        }

        await WriteAtomicallyAsync(credentialsPath, credentials.Root, cancellationToken).ConfigureAwait(false);
        return RefreshResult.Succeeded(token.AccessToken);
    }

    private static async Task<ProfileCredentials> ReadCredentialsAsync(
        string credentialsPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath))
        {
            throw new InvalidOperationException("Claude profile credentials were not found.");
        }

        var json = await File.ReadAllTextAsync(credentialsPath, cancellationToken).ConfigureAwait(false);
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidOperationException("Claude profile credentials were invalid.");
        var oauth = root["claudeAiOauth"] as JsonObject
                    ?? throw new InvalidOperationException("Claude OAuth credentials were not found in the profile.");
        var accessToken = ReadString(oauth, "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Claude OAuth access token was not found in the profile.");
        }

        return new ProfileCredentials(
            root,
            oauth,
            accessToken,
            ReadString(oauth, "refreshToken"),
            ReadInt64(oauth, "expiresAt"),
            ReadString(oauth, "subscriptionType"));
    }

    private static async Task WriteAtomicallyAsync(
        string credentialsPath,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(credentialsPath)
                        ?? throw new InvalidOperationException("Claude profile path was invalid.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(credentialsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                    tempPath,
                    root.ToJsonString(WriteOptions),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempPath, credentialsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool IsUnauthorized(UsageSnapshot snapshot)
        => snapshot.Status == UsageStatus.Failed
           && snapshot.LastError?.Contains("HTTP 401", StringComparison.Ordinal) == true;

    private static string? ReadString(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<string>(out var result) ? result : null;

    private static long? ReadInt64(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<long>(out var result) ? result : null;

    private sealed record ProfileCredentials(
        JsonObject Root,
        JsonObject OAuth,
        string AccessToken,
        string? RefreshToken,
        long? ExpiresAtUnixMs,
        string? SubscriptionType);

    private sealed record RefreshResult(bool Success, string? AccessToken, string? ErrorMessage)
    {
        public static RefreshResult Succeeded(string accessToken) => new(true, accessToken, null);
        public static RefreshResult Failure(string errorMessage) => new(false, null, errorMessage);
    }
}
