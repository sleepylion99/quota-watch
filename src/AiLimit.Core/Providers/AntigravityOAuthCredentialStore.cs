using System.Net.Http;
using System.Runtime.Versioning;
using System.Text.Json;

namespace AiLimit.Core.Providers;

public enum AntigravityOAuthClientOrigin
{
    None,
    Environment,
    IdeCredentialFile,
    UserSavedSettings,
    BundledDefault
}

internal static class AntigravityOAuthCredentialStore
{
    private const string OAuthAccessTokenEnvVar = "ANTIGRAVITY_OAUTH_ACCESS_TOKEN";
    private const string OAuthClientIdEnvVar = "ANTIGRAVITY_OAUTH_CLIENT_ID";
    private const string OAuthClientSecretEnvVar = "ANTIGRAVITY_OAUTH_CLIENT_SECRET";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// Exchanges a refresh token for a fresh access token using the supplied OAuth client values.
    /// Returns <c>null</c> when the inputs are unusable or the exchange fails — this is the
    /// "best effort" variant used by per-account flows that want to surface a failure rather
    /// than propagate an exception. The HTTP status code of a non-success response is
    /// surfaced via <paramref name="failureStatusCode"/> so callers can distinguish a
    /// network problem from a server-side "missing client_secret" 400. Throws
    /// <see cref="OperationCanceledException"/> when the caller cancels.
    /// </summary>
    internal static async Task<TokenRefreshResult?> RefreshAccessTokenAsync(
        string refreshToken,
        AntigravityOAuthClientConfig clientConfig,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var (result, _) = await RefreshAccessTokenAsync(
            refreshToken, clientConfig, httpClient, captureFailureStatus: false, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    internal static async Task<(TokenRefreshResult? Result, System.Net.HttpStatusCode? FailureStatusCode)> RefreshAccessTokenAsync(
        string refreshToken,
        AntigravityOAuthClientConfig clientConfig,
        HttpClient httpClient,
        bool captureFailureStatus,
        CancellationToken cancellationToken)
    {
        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (string.IsNullOrWhiteSpace(refreshToken)
            || string.IsNullOrWhiteSpace(clientConfig.ClientId))
        {
            return (null, null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        var form = new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientConfig.ClientId!,
            ["grant_type"] = "refresh_token"
        };
        if (!string.IsNullOrWhiteSpace(clientConfig.ClientSecret))
        {
            form["client_secret"] = clientConfig.ClientSecret!;
        }

        request.Content = new FormUrlEncodedContent(form);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (null, captureFailureStatus ? response.StatusCode : null);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("access_token", out var accessElement)
                && accessElement.ValueKind == JsonValueKind.String)
            {
                var token = accessElement.GetString();
                if (string.IsNullOrWhiteSpace(token))
                {
                    return (null, null);
                }

                var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                    && expiresInElement.TryGetInt64(out var seconds)
                    ? seconds
                    : 3600;

                return (new TokenRefreshResult(token!, expiresIn), null);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return (null, null);
    }

    internal readonly record struct TokenRefreshResult(string AccessToken, long ExpiresInSeconds);

    public static AntigravityOAuthCredentials? Load()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return new AntigravityWindowsCredentialStore().Load();
    }

    public static string? ResolveAccessToken()
    {
        return ResolveEnvironmentAccessToken() ?? Load()?.AccessToken;
    }

    public static string? ResolveEnvironmentAccessToken()
    {
        var envToken = Environment.GetEnvironmentVariable(OAuthAccessTokenEnvVar);
        return string.IsNullOrWhiteSpace(envToken) ? null : envToken.Trim();
    }

    public static string ResolveOAuthClientSecret(AntigravityOAuthCredentials credentials)
    {
        return TryResolveOAuthClientSecret(credentials)
            ?? throw new InvalidOperationException(
                $"Google Antigravity OAuth client secret was not found. Set {OAuthClientSecretEnvVar} or sign in to Antigravity again.");
    }

    public static string? TryResolveOAuthClientSecret(AntigravityOAuthCredentials credentials)
    {
        var envSecret = Environment.GetEnvironmentVariable(OAuthClientSecretEnvVar);
        if (!string.IsNullOrWhiteSpace(envSecret))
        {
            return envSecret.Trim();
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var activeSecret = DefaultRegistry().GetActive()?.ClientSecret;
        if (!string.IsNullOrWhiteSpace(activeSecret))
        {
            return activeSecret;
        }

        return new AntigravityAppBundleClientStore().Load()?.ClientSecret;
    }

    public static string? ResolveOAuthClientId(AntigravityOAuthCredentials credentials)
    {
        var envClientId = Environment.GetEnvironmentVariable(OAuthClientIdEnvVar);
        if (!string.IsNullOrWhiteSpace(envClientId))
        {
            return envClientId.Trim();
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var activeId = DefaultRegistry().GetActive()?.ClientId;
        if (!string.IsNullOrWhiteSpace(activeId))
        {
            return activeId;
        }

        return new AntigravityAppBundleClientStore().Load()?.ClientId;
    }

    public static AntigravityOAuthClientConfig ResolveClientFromRegistry(AntigravityOAuthClientRegistry registry)
    {
        var active = registry.GetActive();
        return new AntigravityOAuthClientConfig(active?.ClientId, active?.ClientSecret);
    }

    /// <summary>
    /// Resolves the OAuth client values of the registry's active entry (which applies the
    /// one-time legacy migration the rest of the app sees). Used by the failure-hint path so
    /// it reflects the registry instead of the retired single-client store.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static AntigravityOAuthClientConfig ResolveActiveOAuthClientConfig()
        => ResolveClientFromRegistry(DefaultRegistry());

    [SupportedOSPlatform("windows")]
    private static AntigravityOAuthClientRegistry DefaultRegistry()
        => new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AiLimit", "antigravity-oauth-clients.json"),
            AntigravityOAuthClientStore.DefaultPath(),
            new AntigravityAppBundleClientStore().Load() ?? AntigravityBundledOAuthClient.Config,
            new DpapiSecretProtector(),
            legacyLoader: () => new AntigravityOAuthClientStore(AntigravityOAuthClientStore.DefaultPath()).LoadLegacyPlaintext());

    public static AntigravityOAuthClientOrigin ResolveActiveOAuthClientOrigin()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OAuthClientSecretEnvVar)))
        {
            return AntigravityOAuthClientOrigin.Environment;
        }

        if (!OperatingSystem.IsWindows())
        {
            return AntigravityOAuthClientOrigin.None;
        }

        // The registry's active entry is the source the refresh path actually uses. Its
        // built-in entry is the bundled default (either scanned from the IDE or the embedded
        // public client), so report it as BundledDefault rather than UserSavedSettings.
        var active = DefaultRegistry().GetActive();
        if (active is not null && !string.IsNullOrWhiteSpace(active.ClientSecret))
        {
            return active.IsBuiltIn
                ? AntigravityOAuthClientOrigin.BundledDefault
                : AntigravityOAuthClientOrigin.UserSavedSettings;
        }

        if (!string.IsNullOrWhiteSpace(new AntigravityAppBundleClientStore().Load()?.ClientSecret))
        {
            return AntigravityOAuthClientOrigin.IdeCredentialFile;
        }

        return AntigravityOAuthClientOrigin.None;
    }

    internal static AntigravityOAuthClientOrigin ResolveActiveOAuthClientOrigin(
        string? envClientSecret,
        string? userSavedClientSecret,
        string? bundleClientSecret)
    {
        if (!string.IsNullOrWhiteSpace(envClientSecret))
        {
            return AntigravityOAuthClientOrigin.Environment;
        }

        if (!string.IsNullOrWhiteSpace(userSavedClientSecret))
        {
            return AntigravityOAuthClientOrigin.UserSavedSettings;
        }

        if (!string.IsNullOrWhiteSpace(bundleClientSecret))
        {
            return AntigravityOAuthClientOrigin.IdeCredentialFile;
        }

        return AntigravityOAuthClientOrigin.None;
    }
}

internal sealed record AntigravityOAuthCredentials(
    string? AccessToken,
    string? RefreshToken,
    string? ClientId,
    string? ClientSecret,
    DateTimeOffset? ExpiresAt)
{
    public bool ShouldRefresh(DateTimeOffset now)
    {
        return ExpiresAt is not null && ExpiresAt.Value <= now.AddMinutes(1);
    }
}
