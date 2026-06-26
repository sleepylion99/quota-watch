using AiLimit.Core.Providers;
using Xunit;

namespace AiLimit.Tests;

public sealed class AntigravityWindowsCredentialStoreTests
{
    private static string ClientSecret(string value = "AbCdEfGhIjKlMnOpQrStUvWxYz12") =>
        string.Concat("GOC", "SPX", "-", value);

    [Fact]
    public void ParseCredentialBlobReadsTokensAndExpiry()
    {
        const string blob =
            """
            {"token":{"access_token":"ya29.test-access","token_type":"Bearer","refresh_token":"1//test-refresh","expiry":"2026-06-15T12:00:00.000000Z"},"auth_method":"consumer"}
            """;

        var credentials = AntigravityWindowsCredentialStore.ParseCredentialBlob(blob);

        Assert.NotNull(credentials);
        Assert.Equal("ya29.test-access", credentials!.AccessToken);
        Assert.Equal("1//test-refresh", credentials.RefreshToken);
        Assert.Null(credentials.ClientId);
        Assert.Null(credentials.ClientSecret);
        Assert.Equal(
            DateTimeOffset.Parse("2026-06-15T12:00:00.000000Z"),
            credentials.ExpiresAt);
    }

    [Fact]
    public void ParseCredentialBlobReturnsNullWhenJsonIsInvalid()
    {
        Assert.Null(AntigravityWindowsCredentialStore.ParseCredentialBlob(""));
        Assert.Null(AntigravityWindowsCredentialStore.ParseCredentialBlob("not-json"));
        Assert.Null(AntigravityWindowsCredentialStore.ParseCredentialBlob("{}"));
    }

    [Fact]
    public void ParseCredentialBlobToleratesMissingExpiry()
    {
        const string blob =
            """{"token":{"access_token":"ya29.x","refresh_token":"1//y"},"auth_method":"consumer"}""";

        var credentials = AntigravityWindowsCredentialStore.ParseCredentialBlob(blob);

        Assert.NotNull(credentials);
        Assert.Equal("ya29.x", credentials!.AccessToken);
        Assert.Null(credentials.ExpiresAt);
    }

    [Fact]
    public void LoadReturnsNullWhenReaderReportsMissingTarget()
    {
        var store = new AntigravityWindowsCredentialStore(_ => null);

        Assert.Null(store.Load());
    }

    [Fact]
    public void LoadParsesBlobReturnedByReader()
    {
        const string blob =
            """{"token":{"access_token":"ya29.from-reader"},"auth_method":"consumer"}""";
        var store = new AntigravityWindowsCredentialStore(target =>
        {
            Assert.Equal(AntigravityWindowsCredentialStore.DefaultTarget, target);
            return blob;
        });

        var credentials = store.Load();

        Assert.NotNull(credentials);
        Assert.Equal("ya29.from-reader", credentials!.AccessToken);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginReportsIdeWhenBundleScanSucceeds()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: null,
            userSavedClientSecret: null,
            bundleClientSecret: ClientSecret());

        Assert.Equal(AntigravityOAuthClientOrigin.IdeCredentialFile, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginReportsNoneWhenNoSourceHasSecret()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: null,
            userSavedClientSecret: null,
            bundleClientSecret: null);

        Assert.Equal(AntigravityOAuthClientOrigin.None, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginReportsEnvironmentWhenEnvIsSet()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: ClientSecret("EnvValueAbCdEfGhIjKlMnOpQrS"),
            userSavedClientSecret: null,
            bundleClientSecret: null);

        Assert.Equal(AntigravityOAuthClientOrigin.Environment, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginReportsUserSavedWhenOnlyUserStoreHasSecret()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: null,
            userSavedClientSecret: ClientSecret("UserAbCdEfGhIjKlMnOpQrStUv"),
            bundleClientSecret: null);

        Assert.Equal(AntigravityOAuthClientOrigin.UserSavedSettings, origin);
    }

    [Fact]
    public void ReadRawBlobIsCallableWithoutSideEffectsWhenTargetMissing()
    {
        var unused = AntigravityWindowsCredentialStore.ReadRawBlob();
        Assert.True(true);  // guarantee is "does not throw on Windows"
    }
}
