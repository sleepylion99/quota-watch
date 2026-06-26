using AiLimit.Core.Providers;
using Xunit;

namespace AiLimit.Tests;

public class AntigravityOAuthClientRegistryTests
{
    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string value) => "enc:" + value;
        public string? Unprotect(string? value)
            => value is null ? null : value.StartsWith("enc:") ? value[4..] : throw new FormatException();
    }

    private static AntigravityOAuthClientRegistry NewRegistry(string dir)
        => new(
            path: Path.Combine(dir, "clients.json"),
            legacySingleClientPath: Path.Combine(dir, "legacy.json"),
            bundled: new AntigravityOAuthClientConfig("bundled-id", "bundled-secret"),
            protector: new FakeProtector());

    [Fact]
    public void BuiltInClientIsAlwaysPresentAndActiveByDefault()
    {
        using var tmp = new TempDir();
        var reg = NewRegistry(tmp.Path);
        Assert.Contains(reg.List(), c => c.IsBuiltIn);
        Assert.Equal("bundled-id", reg.GetActive()!.ClientId);
    }

    [Fact]
    public void AddSelectAndPersistAcrossInstances()
    {
        using var tmp = new TempDir();
        var entry = NewRegistry(tmp.Path).Add("Work", "work-id", "work-secret");
        var reloaded = NewRegistry(tmp.Path);
        reloaded.SetActive(entry.Key);
        var active = NewRegistry(tmp.Path).GetActive();
        Assert.Equal("work-id", active!.ClientId);
        Assert.Equal("work-secret", active.ClientSecret);
    }

    [Fact]
    public void RemoveBuiltInThrows()
    {
        using var tmp = new TempDir();
        var reg = NewRegistry(tmp.Path);
        var builtIn = reg.List().Single(c => c.IsBuiltIn);
        Assert.Throws<InvalidOperationException>(() => reg.Remove(builtIn.Key));
    }

    [Fact]
    public void CorruptedSecretDoesNotThrow_EntrySurvivesWithNullSecret()
    {
        using var tmp = new TempDir();
        var reg = NewRegistry(tmp.Path);
        var e = reg.Add("Bad", "bad-id", "bad-secret");
        var file = Path.Combine(tmp.Path, "clients.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("enc:bad-secret", "garbage"));
        var reloaded = NewRegistry(tmp.Path);
        var loaded = reloaded.List().Single(c => c.Key == e.Key);
        Assert.Null(loaded.ClientSecret);
        Assert.Equal("bad-id", loaded.ClientId);
    }

    [Fact]
    public void MigratesLegacySingleClientOnce()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "legacy.json"),
            "{\"ClientId\":\"legacy-id\",\"ClientSecret\":\"enc:legacy-secret\"}");
        var reg = NewRegistry(tmp.Path);
        var active = reg.GetActive();
        Assert.Equal("legacy-id", active!.ClientId);
        Assert.Equal("legacy-secret", active.ClientSecret);
        Assert.Contains(reg.List(), c => c.Label == "Imported");
    }

    [Fact]
    public void MigratesViaLegacyLoaderAndReprotectsPlaintext()
    {
        using var tmp = new TempDir();
        var reg = new AntigravityOAuthClientRegistry(
            Path.Combine(tmp.Path, "clients.json"),
            Path.Combine(tmp.Path, "legacy.json"),
            new AntigravityOAuthClientConfig("bundled-id", "bundled-secret"),
            new FakeProtector(),
            legacyLoader: () => new AntigravityOAuthClientConfig("migrated-id", "migrated-secret-plaintext"));

        var active = reg.GetActive();
        Assert.Equal("migrated-id", active!.ClientId);
        Assert.Equal("migrated-secret-plaintext", active.ClientSecret); // round-trips through the protector
        // persisted secret is protected, not plaintext
        var raw = File.ReadAllText(Path.Combine(tmp.Path, "clients.json"));
        Assert.Contains("enc:migrated-secret-plaintext", raw);
        Assert.DoesNotContain("\"migrated-secret-plaintext\"", raw.Replace("enc:migrated-secret-plaintext", ""));
    }
}
