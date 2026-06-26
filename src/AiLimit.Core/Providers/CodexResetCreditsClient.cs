using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AiLimit.Core.Providers;

internal interface ICodexResetCreditsClient
{
    Task<CodexResetCredits?> ReadResetCreditsAsync(CancellationToken cancellationToken);
}

internal sealed record CodexResetCredits(int AvailableCount, IReadOnlyList<CodexResetCreditEntry> Credits);

internal sealed record CodexResetCreditEntry(
    string Id,
    string Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? GrantedAt,
    string? ResetType,
    string? Title)
{
    public bool IsAvailable => string.Equals(Status, "available", StringComparison.OrdinalIgnoreCase);
}

internal sealed class CodexWhamResetCreditsClient : ICodexResetCreditsClient
{
    private const string Endpoint = "https://chatgpt.com/backend-api/wham/rate-limit-reset-credits";
    private readonly HttpClient _httpClient;
    private readonly string _authPath;

    public CodexWhamResetCreditsClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, CodexWhamRateLimitClient.ResolveAuthPath())
    {
    }

    internal CodexWhamResetCreditsClient(HttpClient httpClient, string authPath)
    {
        _httpClient = httpClient;
        _authPath = authPath;
    }

    public async Task<CodexResetCredits?> ReadResetCreditsAsync(CancellationToken cancellationToken)
    {
        var token = CodexWhamRateLimitClient.ReadAccessToken(_authPath);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("originator", "Codex Desktop");
        request.Headers.TryAddWithoutValidation("OAI-Product-Sku", "CODEX");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseResetCredits(body);
    }

    internal static CodexResetCredits ParseResetCredits(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var entries = new List<CodexResetCreditEntry>();
        if (GetProp(root, "credits") is { ValueKind: JsonValueKind.Array } credits)
        {
            foreach (var item in credits.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                entries.Add(new CodexResetCreditEntry(
                    GetString(item, "id") ?? string.Empty,
                    GetString(item, "status") ?? "unknown",
                    GetDate(item, "expires_at", "expiresAt"),
                    GetDate(item, "granted_at", "grantedAt"),
                    GetString(item, "reset_type", "resetType"),
                    GetString(item, "title")));
            }
        }

        var availableCount = GetInt(root, "available_count", "availableCount")
            ?? entries.Count(entry => entry.IsAvailable);

        return new CodexResetCredits(availableCount, entries);
    }

    private static JsonElement? GetProp(JsonElement source, params string[] names)
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

    private static string? GetString(JsonElement source, params string[] names)
    {
        return GetProp(source, names) is { ValueKind: JsonValueKind.String } value
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement source, params string[] names)
    {
        return GetProp(source, names) is { ValueKind: JsonValueKind.Number } value
            && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static DateTimeOffset? GetDate(JsonElement source, params string[] names)
    {
        return GetProp(source, names) is { ValueKind: JsonValueKind.String } value
            && DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
            ? parsed
            : null;
    }
}
