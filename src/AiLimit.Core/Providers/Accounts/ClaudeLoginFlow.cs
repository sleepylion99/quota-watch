using System.Net.Http;
using AiLimit.Core.Providers;

namespace AiLimit.Core.Providers.Accounts;

public enum ClaudeLoginOutcome { Added, Cancelled, AuthFailed, ExchangeFailed, WriteFailed }

public sealed record ClaudeLoginResult(ClaudeLoginOutcome Outcome, string? ProfilePath = null, string? ErrorMessage = null);

public sealed record ClaudeBeginResult(string AuthUrl, bool BrowserOpened);

public sealed class ClaudeLoginFlow
{
    private readonly Action<string> _openBrowser;
    private readonly ClaudeOAuthCredentialStore _credentials;
    private readonly Func<ClaudeCredential, string> _writeProfile;
    private readonly Func<string> _stateFactory;

    private string? _state;
    private string? _verifier;

    public ClaudeLoginFlow(
        Action<string> openBrowser,
        ClaudeOAuthCredentialStore credentials,
        Func<ClaudeCredential, string> writeProfile,
        Func<string>? stateFactory = null)
    {
        _openBrowser = openBrowser;
        _credentials = credentials;
        _writeProfile = writeProfile;
        _stateFactory = stateFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public ClaudeBeginResult BeginSignIn()
    {
        _state = _stateFactory();
        var pkce = Pkce.Create();
        _verifier = pkce.Verifier;
        var url = BuildAuthUrl(_state, pkce.Challenge);

        var opened = true;
        try { _openBrowser(url); }
        catch { opened = false; }   // manual-paste flow: user can open the URL themselves
        return new ClaudeBeginResult(url, opened);
    }

    public async Task<ClaudeLoginResult> CompleteSignInAsync(string pastedCode, CancellationToken cancellationToken)
    {
        if (_state is null || _verifier is null)
        {
            return new ClaudeLoginResult(ClaudeLoginOutcome.AuthFailed, ErrorMessage: "sign-in not started");
        }

        var (code, state) = ParsePastedCode(pastedCode);
        if (string.IsNullOrEmpty(code))
        {
            return new ClaudeLoginResult(ClaudeLoginOutcome.AuthFailed, ErrorMessage: "empty code");
        }
        if (state is not null && !string.Equals(state, _state, StringComparison.Ordinal))
        {
            return new ClaudeLoginResult(ClaudeLoginOutcome.AuthFailed, ErrorMessage: "state mismatch");
        }

        ClaudeTokenResult token;
        try
        {
            token = await _credentials
                .ExchangeCodeAsync(code, _state!, _verifier!, ClaudeOAuthCredentialStore.ManualRedirectUri, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ClaudeLoginResult(ClaudeLoginOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            return new ClaudeLoginResult(
                ClaudeLoginOutcome.ExchangeFailed,
                ErrorMessage: ex is OperationCanceledException ? "timeout" : $"{ex.GetType().Name}: {ex.Message}");
        }

        if (!token.Success || string.IsNullOrEmpty(token.AccessToken))
        {
            return new ClaudeLoginResult(ClaudeLoginOutcome.ExchangeFailed, ErrorMessage: token.ErrorMessage);
        }

        try
        {
            var path = _writeProfile(new ClaudeCredential(
                token.AccessToken!, token.RefreshToken, token.ExpiresAtUnixMs,
                Scopes: ClaudeOAuthCredentialStore.Scopes));
            return new ClaudeLoginResult(ClaudeLoginOutcome.Added, ProfilePath: path);
        }
        catch (Exception ex)
        {
            return new ClaudeLoginResult(ClaudeLoginOutcome.WriteFailed, ErrorMessage: ex.Message);
        }
    }

    private static (string Code, string? State) ParsePastedCode(string pasted)
    {
        var trimmed = pasted.Trim();
        var hash = trimmed.IndexOf('#');
        return hash >= 0 ? (trimmed[..hash], trimmed[(hash + 1)..]) : (trimmed, null);
    }

    private static string BuildAuthUrl(string state, string challenge)
    {
        var p = new Dictionary<string, string>
        {
            ["code"] = "true",
            ["client_id"] = ClaudeOAuthCredentialStore.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = ClaudeOAuthCredentialStore.ManualRedirectUri,
            ["scope"] = string.Join(' ', ClaudeOAuthCredentialStore.Scopes),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        var query = string.Join("&", p.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{ClaudeOAuthCredentialStore.AuthorizeEndpoint}?{query}";
    }
}
