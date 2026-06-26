using AiLimit.Core.Providers;

namespace AiLimit.Tests;

public sealed class AntigravityOAuthCredentialStoreTests
{
    private sealed class PassthroughProtector : ISecretProtector
    {
        public string Protect(string value) => value;
        public string? Unprotect(string? value) => value;
    }

    [Fact]
    public void ResolutionPrefersActiveRegistryClientOverBundle()
    {
        using var tmp = new TempDir();
        var registry = new AntigravityOAuthClientRegistry(
            Path.Combine(tmp.Path, "clients.json"),
            Path.Combine(tmp.Path, "legacy.json"),
            new AntigravityOAuthClientConfig("bundle-id", "bundle-secret"),
            new PassthroughProtector());
        var e = registry.Add("Mine", "mine-id", "mine-secret");
        registry.SetActive(e.Key);

        var id = AntigravityOAuthCredentialStore.ResolveClientFromRegistry(registry).ClientId;
        Assert.Equal("mine-id", id);
    }

    [Fact]
    public void OAuthClientOriginsOnlyContainConfiguredSources()
    {
        var names = Enum.GetNames<AntigravityOAuthClientOrigin>();
        Assert.Equal(
            ["None", "Environment", "IdeCredentialFile", "UserSavedSettings", "BundledDefault"],
            names);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginPicksEnvironmentWhenSecretIsSet()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: "env-secret",
            userSavedClientSecret: "user-secret",
            bundleClientSecret: "bundle-secret");

        Assert.Equal(AntigravityOAuthClientOrigin.Environment, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginPicksUserSavedWhenEnvironmentLacksSecret()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: null,
            userSavedClientSecret: "user-secret",
            bundleClientSecret: "bundle-secret");

        Assert.Equal(AntigravityOAuthClientOrigin.UserSavedSettings, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginPicksBundleWhenOnlyBundleHasSecret()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: null,
            userSavedClientSecret: null,
            bundleClientSecret: "bundle-secret");

        Assert.Equal(AntigravityOAuthClientOrigin.IdeCredentialFile, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginReturnsNoneWhenNothingHasSecret()
    {
        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            envClientSecret: null,
            userSavedClientSecret: null,
            bundleClientSecret: null);

        Assert.Equal(AntigravityOAuthClientOrigin.None, origin);
    }
}
