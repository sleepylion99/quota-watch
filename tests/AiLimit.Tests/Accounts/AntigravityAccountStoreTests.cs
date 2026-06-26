using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AntigravityAccountStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"acct-{Guid.NewGuid():N}.json");

    [Fact]
    public void LoadReturnsEmptyListWhenFileMissing()
    {
        var path = TempPath();
        var store = new AntigravityAccountStore(path);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void AddPersistsRefreshTokenEncrypted()
    {
        var path = TempPath();
        var store = new AntigravityAccountStore(path);

        var added = store.Add(
            displayName: "alice",
            email: "alice@example.com",
            refreshToken: "1//refresh-abc");

        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.Equal("gemini-pro", added.ProviderKey);

        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("1//refresh-abc", raw);

        var loaded = store.Load();
        var single = Assert.Single(loaded);
        Assert.Equal(added.Id, single.Id);
        Assert.Equal("alice@example.com", single.Email);
        Assert.Equal("1//refresh-abc", store.ReadRefreshToken(single.Id));

        File.Delete(path);
    }

    [Fact]
    public void AddRejectsDuplicateRefreshToken()
    {
        var path = TempPath();
        var store = new AntigravityAccountStore(path);
        store.Add("alice", null, "1//rt");

        Assert.Throws<InvalidOperationException>(() => store.Add("alice2", null, "1//rt"));

        File.Delete(path);
    }

    [Fact]
    public void RemoveByIdDropsTheRecordAndKeepsOthers()
    {
        var path = TempPath();
        var store = new AntigravityAccountStore(path);
        var keep = store.Add("keep", null, "1//keep");
        var drop = store.Add("drop", null, "1//drop");

        store.Remove(drop.Id);

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal(keep.Id, loaded[0].Id);

        File.Delete(path);
    }

    [Fact]
    public void ReadRefreshTokenReturnsNullForUnknownId()
    {
        var path = TempPath();
        var store = new AntigravityAccountStore(path);
        Assert.Null(store.ReadRefreshToken(Guid.NewGuid()));
    }

    [Fact]
    public void ReadRefreshTokenReturnsNullForCorruptCiphertext()
    {
        var path = TempPath();
        var id = Guid.NewGuid();
        WriteCorruptAccount(path, id);
        var store = new AntigravityAccountStore(path);

        var token = store.ReadRefreshToken(id);

        Assert.Null(token);
        File.Delete(path);
    }

    [Fact]
    public void AddStillWorksWhenAnExistingCiphertextIsCorrupt()
    {
        var path = TempPath();
        WriteCorruptAccount(path, Guid.NewGuid());
        var store = new AntigravityAccountStore(path);

        var added = store.Add("bob", "bob@example.com", "1//valid");

        Assert.Equal("1//valid", store.ReadRefreshToken(added.Id));
        Assert.Equal(2, store.Load().Count);
        File.Delete(path);
    }

    [Fact]
    public void AddEmbedsPerAccountSaltInFileAndRoundTrips()
    {
        var path = TempPath();
        var store = new AntigravityAccountStore(path);

        var a = store.Add("alice", "a@example.com", "1//salt-a");
        var b = store.Add("bob", "b@example.com", "1//salt-b");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var accounts = doc.RootElement.GetProperty("accounts");
        var saltA = accounts[0].GetProperty("token_salt").GetString();
        var saltB = accounts[1].GetProperty("token_salt").GetString();

        // Each account carries its own salt inside the file, so reads no longer
        // depend on the shared registry entropy that previously desynced.
        Assert.False(string.IsNullOrWhiteSpace(saltA));
        Assert.False(string.IsNullOrWhiteSpace(saltB));
        Assert.NotEqual(saltA, saltB);
        Assert.Equal("1//salt-a", store.ReadRefreshToken(a.Id));
        Assert.Equal("1//salt-b", store.ReadRefreshToken(b.Id));

        File.Delete(path);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ReadRefreshTokenDecryptsUsingFileSaltNotRegistryEntropy()
    {
        var path = TempPath();
        var id = Guid.NewGuid();
        WriteSaltedAccount(path, id, "1//file-salted");
        var store = new AntigravityAccountStore(path);

        // The salt lives only in the file (never in the registry), so a successful
        // read proves the read path is driven by the file salt — the desync that
        // produced "Account removed." is structurally impossible for salted entries.
        Assert.Equal("1//file-salted", store.ReadRefreshToken(id));

        File.Delete(path);
    }

    [Fact]
    public void ReadRefreshTokenLookupReportsNotStoredForUnknownId()
    {
        var store = new AntigravityAccountStore(TempPath());

        var lookup = store.ReadRefreshTokenLookup(Guid.NewGuid());

        Assert.Equal(RefreshTokenStatus.NotStored, lookup.Status);
        Assert.Null(lookup.Token);
    }

    [Fact]
    public void ReadRefreshTokenLookupReportsUndecryptableForCorruptCiphertext()
    {
        var path = TempPath();
        var id = Guid.NewGuid();
        WriteCorruptAccount(path, id);
        var store = new AntigravityAccountStore(path);

        var lookup = store.ReadRefreshTokenLookup(id);

        // A stored-but-unreadable token must NOT be reported as a removed account.
        Assert.Equal(RefreshTokenStatus.Undecryptable, lookup.Status);
        Assert.Null(lookup.Token);
        File.Delete(path);
    }

    [Fact]
    public void ReadRefreshTokenLookupReportsAvailableForValidToken()
    {
        var path = TempPath();
        var store = new AntigravityAccountStore(path);
        var added = store.Add("alice", null, "1//ok");

        var lookup = store.ReadRefreshTokenLookup(added.Id);

        Assert.Equal(RefreshTokenStatus.Available, lookup.Status);
        Assert.Equal("1//ok", lookup.Token);
        File.Delete(path);
    }

    [SupportedOSPlatform("windows")]
    private static void WriteSaltedAccount(string path, Guid id, string plaintextToken)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var saltB64 = Convert.ToBase64String(salt);
        var cipher = Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintextToken),
            salt,
            DataProtectionScope.CurrentUser));
        File.WriteAllText(
            path,
            $$"""
            {
              "accounts": [
                {
                  "id": "{{id}}",
                  "name": "salted",
                  "email": null,
                  "refresh_token": "{{cipher}}",
                  "token_salt": "{{saltB64}}",
                  "created_at": "2026-06-24T00:00:00Z",
                  "last_synced_at": "2026-06-24T00:00:00Z"
                }
              ]
            }
            """);
    }

    private static void WriteCorruptAccount(string path, Guid id)
    {
        File.WriteAllText(
            path,
            $$"""
            {
              "accounts": [
                {
                  "id": "{{id}}",
                  "name": "broken",
                  "email": null,
                  "refresh_token": "not-base64",
                  "created_at": "2026-06-21T00:00:00Z",
                  "last_synced_at": "2026-06-21T00:00:00Z"
                }
              ]
            }
            """);
    }
}
