using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AiLimit.Core.Providers;

public readonly record struct ClaudeTokenResult(
    bool Success, string? AccessToken, string? RefreshToken, long? ExpiresAtUnixMs, string? ErrorMessage);

public sealed record ClaudeAccount(string? Email, string? DisplayName, bool HasMax, bool HasPro);

public sealed class ClaudeOAuthCredentialStore
{
    // Claude Code public OAuth client. Verified during manual end-to-end sign-in;
    // unit tests mock the HTTP handler so they do not depend on these values.
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    public const string AuthorizeEndpoint = "https://claude.ai/oauth/authorize";
    public const string TokenEndpoint = "https://console.anthropic.com/v1/oauth/token";
    public const string ManualRedirectUri = "https://console.anthropic.com/oauth/code/callback";
    public const string ProfileEndpoint = "https://api.anthropic.com/api/oauth/profile";
    public static readonly string[] Scopes = { "user:inference", "user:profile" };

    private readonly HttpClient _httpClient;

    public ClaudeOAuthCredentialStore(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public Task<ClaudeTokenResult> ExchangeCodeAsync(string code, string state, string verifier, string redirectUri, CancellationToken ct)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = code,
            ["state"] = state,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        }, ct);

    public Task<ClaudeTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
        }, ct);

    /// <summary>Reads the signed-in account (email/plan) from the Claude OAuth profile endpoint. Returns null on any failure.</summary>
    public async Task<ClaudeAccount?> FetchAccountAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            return new ClaudeAccount(
                Email: ReadString(account, "email"),
                DisplayName: ReadString(account, "display_name") ?? ReadString(account, "full_name"),
                HasMax: account.TryGetProperty("has_claude_max", out var m) && m.ValueKind == JsonValueKind.True,
                HasPro: account.TryGetProperty("has_claude_pro", out var p) && p.ValueKind == JsonValueKind.True);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<ClaudeTokenResult> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(form), System.Text.Encoding.UTF8, "application/json"),
        };
        using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            return new ClaudeTokenResult(false, null, null, null, DescribeError((int)resp.StatusCode, body));
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var access = ReadString(root, "access_token");
            var refresh = ReadString(root, "refresh_token");
            long? expiresAt = null;
            if (root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number && ei.TryGetInt64(out var seconds))
            {
                expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeMilliseconds();
            }
            if (string.IsNullOrEmpty(access))
            {
                return new ClaudeTokenResult(false, null, null, null, DescribeError((int)resp.StatusCode, body));
            }
            return new ClaudeTokenResult(true, access, refresh, expiresAt, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new ClaudeTokenResult(false, null, null, null, $"HTTP {(int)resp.StatusCode}");
        }
    }

    private static string DescribeError(int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    var err = e.GetString();
                    var description = ReadString(root, "error_description");
                    if (!string.IsNullOrEmpty(err))
                    {
                        return string.IsNullOrEmpty(description) ? $"HTTP {status}: {err}" : $"HTTP {status}: {err} — {description}";
                    }
                }
                else if (e.ValueKind == JsonValueKind.Object)
                {
                    // Anthropic shape: {"error":{"type":"...","message":"..."}}
                    var detail = ReadString(e, "message") ?? ReadString(e, "type");
                    if (!string.IsNullOrEmpty(detail))
                    {
                        return $"HTTP {status}: {detail}";
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
        }
        return $"HTTP {status}";
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
