using System.IO;
using AiLimit.Core.Providers;
using Xunit;

namespace AiLimit.Tests;

public sealed class AntigravityAppBundleClientStoreTests
{
    private static string ClientId(string slug) =>
        string.Concat("111222333444-", slug, ".apps.googleusercontent.com");

    private static string ClientSecret(string value = "AbCdEfGhIjKlMnOpQrStUvWxYz12") =>
        string.Concat("GOC", "SPX", "-", value);

    [Fact]
    public void ExtractClientFromContentReadsIdAndSecret()
    {
        var clientId = string.Concat("987654321012-", "abcXYZ_test", ".apps.googleusercontent.com");
        var clientSecret = ClientSecret();
        var mainJs = $$"""
            var something = require("vs/platform/cloudCode/common/oauthClient.js");
            const CLIENT_ID = "{{clientId}}";
            const CLIENT_SECRET = "{{clientSecret}}";
            """;

        var client = AntigravityAppBundleClientStore.ExtractClient(mainJs);

        Assert.NotNull(client);
        Assert.Equal(clientId, client!.ClientId);
        Assert.Equal(clientSecret, client.ClientSecret);
    }

    [Fact]
    public void ExtractClientReturnsNullWhenNoIdPresent()
    {
        Assert.Null(AntigravityAppBundleClientStore.ExtractClient(""));
        Assert.Null(AntigravityAppBundleClientStore.ExtractClient("nothing-to-see-here"));
        Assert.Null(AntigravityAppBundleClientStore.ExtractClient($"{ClientSecret()} alone"));
    }

    [Fact]
    public void ExtractClientRejectsSecretShorterThan28Chars()
    {
        var content =
            $"{ClientId("test")} GOCSPX-TooShort";

        Assert.Null(AntigravityAppBundleClientStore.ExtractClient(content));
    }

    [Fact]
    public void LoadScansFirstCandidateWithMatchingContent()
    {
        var temp = CreateTempBundle(out var mainJs);
        var clientId = ClientId("good");
        var clientSecret = ClientSecret();
        File.WriteAllText(mainJs,
            $"stuff {clientId} {clientSecret} more");
        var store = new AntigravityAppBundleClientStore(
            installRoots: [temp],
            signatureVerifier: _ => true);

        var client = store.Load();

        Assert.NotNull(client);
        Assert.Equal(clientId, client!.ClientId);
        Assert.Equal(clientSecret, client.ClientSecret);
    }

    [Fact]
    public void LoadReturnsNullWhenSignatureVerifierRejects()
    {
        var temp = CreateTempBundle(out var mainJs);
        File.WriteAllText(mainJs,
            $"{ClientId("bad")} {ClientSecret()}");
        var store = new AntigravityAppBundleClientStore(
            installRoots: [temp],
            signatureVerifier: _ => false);

        Assert.Null(store.Load());
    }

    [Fact]
    public void LoadReturnsNullWhenInstallRootDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new AntigravityAppBundleClientStore(
            installRoots: [missing],
            signatureVerifier: _ => true);

        Assert.Null(store.Load());
    }

    // NOTE: This test requires Windows admin or Developer Mode to create the symlink.
    // xUnit 2.9.3 has no native Skip mechanism (Assert.Skip is xUnit v3; Xunit.SkippableFact
    // is a separate package we don't currently reference), so when symlink creation fails
    // the test returns early and is reported as Passed. The test name encodes this caveat.
    [Fact]
    public void LoadSkipsReparsePoints_RequiresAdminOrDevModeForSymlinkCreation()
    {
        var temp = CreateTempBundle(out var mainJs);
        File.WriteAllText(mainJs,
            $"{ClientId("x")} {ClientSecret()}");

        var linkTarget = Path.Combine(temp, "resources", "app", "out", "main.linked.js");
        try
        {
            File.CreateSymbolicLink(linkTarget, mainJs);
        }
        catch
        {
            // Symlink creation is a privileged operation on Windows. Best we can do on
            // xUnit 2.x is bail out — the test name documents this limitation so a
            // green result here is not silently misleading.
            return;
        }

        var store = new AntigravityAppBundleClientStore(
            installRoots: [temp],
            signatureVerifier: _ => true,
            relativeCandidatePaths: ["resources/app/out/main.linked.js"]);

        Assert.Null(store.Load());
    }

    private static string CreateTempBundle(out string mainJsPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "qw-antigrav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "resources", "app", "out"));
        mainJsPath = Path.Combine(temp, "resources", "app", "out", "main.js");
        return temp;
    }
}
