using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private readonly IClaudeUsageClient _client;

    public ClaudeUsageProvider(string providerId, string displayName)
        : this(providerId, displayName, new ClaudeOAuthUsageClient())
    {
    }

    internal ClaudeUsageProvider(string providerId, string displayName, IClaudeUsageClient client)
    {
        Descriptor = new ProviderDescriptor(providerId, displayName, true);
        _client = client;
    }

    public ProviderDescriptor Descriptor { get; }

    public async Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var usage = await _client.ReadUsageAsync(cancellationToken).ConfigureAwait(false);
            var windows = BuildWindows(usage).ToList();
            if (windows.Count == 0)
            {
                return Failed("Claude usage API returned no rate-limit windows.");
            }

            return new UsageSnapshot(
                Descriptor.Id,
                Descriptor.DisplayName,
                DateTimeOffset.Now,
                UsageSource.Agent,
                UsageStatus.Fresh,
                windows,
                AccountKey: usage.AccountKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ex.Message);
        }
    }

    private IEnumerable<UsageWindow> BuildWindows(ClaudeOAuthUsageResponse usage)
    {
        if (usage.FiveHour is not null)
        {
            yield return BuildWindow("five-hour", "Current session", usage.FiveHour);
        }

        if (Descriptor.Id == "claude")
        {
            if (usage.SevenDay is not null)
            {
                yield return BuildWindow("weekly", "Current week (all models)", usage.SevenDay);
            }

            if (usage.SevenDaySonnet is not null)
            {
                yield return BuildWindow("weekly-sonnet", "Current week (Sonnet only)", usage.SevenDaySonnet);
            }

            if (usage.SevenDayOpus is not null)
            {
                yield return BuildWindow("weekly-opus", "Current week (Opus only)", usage.SevenDayOpus);
            }

            if (usage.SevenDayRoutines is not null)
            {
                yield return BuildWindow("weekly-routines", "Current week (Routines)", usage.SevenDayRoutines);
            }

            if (usage.SevenDayCowork is not null)
            {
                yield return BuildWindow("weekly-cowork", "Current week (Cowork)", usage.SevenDayCowork);
            }

            yield break;
        }

        if (Descriptor.Id.EndsWith("-opus", StringComparison.Ordinal) && usage.SevenDayOpus is not null)
        {
            yield return BuildWindow("weekly-opus", "Current week (Opus only)", usage.SevenDayOpus);
        }
        else if (usage.SevenDay is not null)
        {
            yield return BuildWindow("weekly", "Current week (all models)", usage.SevenDay);
        }
    }

    private static UsageWindow BuildWindow(string id, string label, ClaudeOAuthUsageWindow window)
    {
        var preciseUsedPercent = Math.Clamp(window.Utilization ?? 0, 0, 100);
        var usedPercent = (int)Math.Round(preciseUsedPercent, MidpointRounding.AwayFromZero);
        return new UsageWindow(
            id,
            label,
            usedPercent,
            ParseReset(window.ResetsAt),
            null,
            "high",
            IsUsedPercent: true,
            PrecisePercent: preciseUsedPercent);
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

    private static DateTimeOffset? ParseReset(string? value)
    {
        return DateTimeOffset.TryParse(value, out var resetAt) ? resetAt : null;
    }
}

internal interface IClaudeUsageClient
{
    Task<ClaudeOAuthUsageResponse> ReadUsageAsync(CancellationToken cancellationToken);
}

internal sealed class ClaudeOAuthUsageClient : IClaudeUsageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;

    public ClaudeOAuthUsageClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
    {
    }

    internal ClaudeOAuthUsageClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ClaudeOAuthUsageResponse> ReadUsageAsync(CancellationToken cancellationToken)
    {
        var accessToken = ResolveAccessToken();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Claude OAuth credentials were not found. Run Claude Code login first.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("claude-code/2.1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude OAuth usage request failed: HTTP {(int)response.StatusCode}.");
        }

        return (JsonSerializer.Deserialize<ClaudeOAuthUsageResponse>(body, JsonOptions)
                ?? throw new InvalidOperationException("Claude OAuth usage response was empty."))
            with
            {
                AccountKey = AccountKeyHash.FromSecret("claude", accessToken)
            };
    }

    internal static string? ResolveAccessToken()
    {
        var envToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            return envToken.Trim();
        }

        foreach (var path in CandidateCredentialFiles())
        {
            var token = TryReadAccessToken(path);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateCredentialFiles()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (var root in configured.Split([',', Path.PathSeparator], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return Path.Combine(root, ".credentials.json");
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            yield return Path.Combine(profile, ".claude", ".credentials.json");
        }
    }

    private static string? TryReadAccessToken(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                return null;
            }

            return oauth.TryGetProperty("accessToken", out var token)
                ? token.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record ClaudeOAuthUsageResponse(
    [property: JsonPropertyName("five_hour")] ClaudeOAuthUsageWindow? FiveHour,
    [property: JsonPropertyName("seven_day")] ClaudeOAuthUsageWindow? SevenDay,
    [property: JsonPropertyName("seven_day_sonnet")] ClaudeOAuthUsageWindow? SevenDaySonnet,
    [property: JsonPropertyName("seven_day_opus")] ClaudeOAuthUsageWindow? SevenDayOpus,
    [property: JsonPropertyName("seven_day_routines")] ClaudeOAuthUsageWindow? SevenDayRoutines = null,
    [property: JsonPropertyName("seven_day_cowork")] ClaudeOAuthUsageWindow? SevenDayCowork = null,
    string? AccountKey = null);

internal sealed record ClaudeOAuthUsageWindow(
    double? Utilization,
    [property: JsonPropertyName("resets_at")] string? ResetsAt);
