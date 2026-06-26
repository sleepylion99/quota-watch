using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AccountTrashStoreTests
{
    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"account-trash-{label}-{Guid.NewGuid():N}.json");

    [Fact]
    public void PutAndListByProviderRoundTripsAccountIdentity()
    {
        var path = TempPath("roundtrip");
        try
        {
            var store = new AccountTrashStore(path);
            var id = Guid.NewGuid();
            var deletedAt = DateTimeOffset.UtcNow;

            store.Put(new TrashedAccountRecord(
                id, "gemini-pro", "alice", "alice@example.com", deletedAt, "resource"));

            var item = Assert.Single(store.List("gemini-pro"));
            Assert.Equal(id, item.Id);
            Assert.Equal("alice", item.DisplayName);
            Assert.Equal("alice@example.com", item.Email);
            Assert.Equal("resource", item.ResourcePath);
            Assert.Empty(store.List("codex"));
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void PutReplacesSameProviderAndId()
    {
        var path = TempPath("replace");
        try
        {
            var store = new AccountTrashStore(path);
            var id = Guid.NewGuid();
            store.Put(new TrashedAccountRecord(id, "codex", "old", null, DateTimeOffset.UtcNow));
            store.Put(new TrashedAccountRecord(id, "codex", "new", "new@example.com", DateTimeOffset.UtcNow));

            var item = Assert.Single(store.List("codex"));
            Assert.Equal("new", item.DisplayName);
            Assert.Equal("new@example.com", item.Email);
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void RemoveIsIdempotent()
    {
        var path = TempPath("remove");
        try
        {
            var store = new AccountTrashStore(path);
            var id = Guid.NewGuid();
            store.Put(new TrashedAccountRecord(id, "codex", "profile", null, DateTimeOffset.UtcNow));

            store.Remove("codex", id);
            store.Remove("codex", id);

            Assert.Empty(store.List("codex"));
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void LoadReturnsEmptyForCorruptJson()
    {
        var path = TempPath("corrupt");
        try
        {
            File.WriteAllText(path, "{not-json");
            var store = new AccountTrashStore(path);

            Assert.Empty(store.List());
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }
}
