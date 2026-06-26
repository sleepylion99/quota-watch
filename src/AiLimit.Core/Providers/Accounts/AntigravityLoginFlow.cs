using System.Net.Http;
using System.Text.Json;

namespace AiLimit.Core.Providers.Accounts;

public enum AntigravityLoginOutcome
{
    Added, Duplicate, NoActiveClient, Cancelled, AuthFailed, ExchangeFailed, UserInfoFailed
}

public sealed record AntigravityLoginResult(
    AntigravityLoginOutcome Outcome, string? Email = null, string? ErrorMessage = null);

public sealed class AntigravityLoginFlow
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly string[] Scopes =
    {
        "https://www.googleapis.com/auth/cloud-platform",
        "https://www.googleapis.com/auth/userinfo.email",
        "https://www.googleapis.com/auth/userinfo.profile",
        "https://www.googleapis.com/auth/cclog",
        "https://www.googleapis.com/auth/experimentsandconfigs",
        "https://www.googleapis.com/auth/aicode",
    };

    private readonly Func<AntigravityOAuthClientConfig?> _activeClient;
    private readonly Func<ILoopbackOAuthListener> _listenerFactory;
    private readonly Action<string> _openBrowser;
    private readonly HttpClient _httpClient;
    private readonly Func<string, CancellationToken, Task<string?>> _fetchEmail;
    private readonly Func<string, string, bool> _addAccount;
    private readonly Func<string> _stateFactory;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _networkTimeout;

    public AntigravityLoginFlow(
        Func<AntigravityOAuthClientConfig?> activeClient,
        Func<ILoopbackOAuthListener> listenerFactory,
        Action<string> openBrowser,
        HttpClient httpClient,
        Func<string, CancellationToken, Task<string?>> fetchEmail,
        Func<string, string, bool> addAccount,
        Func<string>? stateFactory = null,
        TimeSpan? timeout = null,
        TimeSpan? networkTimeout = null)
    {
        _activeClient = activeClient;
        _listenerFactory = listenerFactory;
        _openBrowser = openBrowser;
        _httpClient = httpClient;
        _fetchEmail = fetchEmail;
        _addAccount = addAccount;
        _stateFactory = stateFactory ?? (() => Guid.NewGuid().ToString("N"));
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
        _networkTimeout = networkTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<AntigravityLoginResult> SignInAsync(CancellationToken cancellationToken)
    {
        var client = _activeClient();
        if (client is null || string.IsNullOrWhiteSpace(client.ClientId))
        {
            return new AntigravityLoginResult(AntigravityLoginOutcome.NoActiveClient);
        }

        var state = _stateFactory();
        var pkce = Pkce.Create();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            ILoopbackOAuthListener listenerOrNull;
            try
            {
                listenerOrNull = _listenerFactory();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.AuthFailed, ErrorMessage: $"listener: {ex.Message}");
            }

            using var listener = listenerOrNull;
            var authUrl = BuildAuthUrl(client.ClientId!, listener.RedirectUri, state, pkce.Challenge);
            try
            {
                _openBrowser(authUrl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.AuthFailed, ErrorMessage: $"browser: {ex.Message}");
            }

            LoopbackCallback cb;
            try
            {
                cb = await listener.WaitForCallbackAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OAuthCallbackException ex)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.AuthFailed, ErrorMessage: ex.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.Cancelled);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.AuthFailed, ErrorMessage: "timeout");
            }
            catch (Exception ex)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.AuthFailed, ErrorMessage: ex.Message);
            }

            if (cb.State != state || string.IsNullOrEmpty(cb.Code))
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.AuthFailed, ErrorMessage: "state mismatch");
            }

            // The authorization deadline only guarded the browser callback. Token exchange and
            // account lookup get their own network deadline, independent of the 10-minute auth
            // window and of any shared HttpClient timeout, and stay cancellable by the caller.
            using var networkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            networkCts.CancelAfter(_networkTimeout);

            TokenExchangeResult exchange;
            try
            {
                exchange = await ExchangeCodeAsync(client, listener.RedirectUri, cb.Code, pkce.Verifier, networkCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.Cancelled);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
            {
                return new AntigravityLoginResult(
                    AntigravityLoginOutcome.ExchangeFailed,
                    ErrorMessage: ex is OperationCanceledException ? "timeout" : ex.Message);
            }

            if (!exchange.Success)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.ExchangeFailed, ErrorMessage: exchange.ErrorMessage);
            }
            if (string.IsNullOrEmpty(exchange.RefreshToken))
            {
                return new AntigravityLoginResult(
                    AntigravityLoginOutcome.ExchangeFailed, ErrorMessage: "no refresh token — re-consent required");
            }

            string? email;
            try
            {
                email = await _fetchEmail(exchange.AccessToken!, networkCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.Cancelled);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
            {
                return new AntigravityLoginResult(
                    AntigravityLoginOutcome.UserInfoFailed,
                    ErrorMessage: ex is OperationCanceledException ? "timeout" : ex.Message);
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return new AntigravityLoginResult(AntigravityLoginOutcome.UserInfoFailed);
            }

            var isDuplicate = _addAccount(email, exchange.RefreshToken!);
            return isDuplicate
                ? new AntigravityLoginResult(AntigravityLoginOutcome.Duplicate, email)
                : new AntigravityLoginResult(AntigravityLoginOutcome.Added, email);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AntigravityLoginResult(AntigravityLoginOutcome.Cancelled);
        }
    }

    private static string BuildAuthUrl(string clientId, string redirectUri, string state, string challenge)
    {
        var p = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        var query = string.Join("&", p.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{AuthEndpoint}?{query}";
    }

    private readonly record struct TokenExchangeResult(
        bool Success, string? AccessToken, string? RefreshToken, string? ErrorMessage);

    private async Task<TokenExchangeResult> ExchangeCodeAsync(
        AntigravityOAuthClientConfig client, string redirectUri, string code, string verifier, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId!,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        };
        if (!string.IsNullOrWhiteSpace(client.ClientSecret))
        {
            form["client_secret"] = client.ClientSecret!;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            return new TokenExchangeResult(false, null, null, DescribeTokenError((int)resp.StatusCode, body));
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            if (string.IsNullOrEmpty(accessToken))
            {
                return new TokenExchangeResult(false, null, null, DescribeTokenError((int)resp.StatusCode, body));
            }
            return new TokenExchangeResult(true, accessToken, refreshToken, null);
        }
        catch (JsonException)
        {
            return new TokenExchangeResult(false, null, null, $"HTTP {(int)resp.StatusCode}");
        }
    }

    private static string DescribeTokenError(int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            if (!string.IsNullOrEmpty(error))
            {
                return string.IsNullOrEmpty(description) ? $"HTTP {status}: {error}" : $"HTTP {status}: {error} — {description}";
            }
        }
        catch (JsonException)
        {
            // fall through to status-only message
        }
        return $"HTTP {status}";
    }
}
