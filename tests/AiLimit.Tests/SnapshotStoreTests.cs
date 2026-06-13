using AiLimit.Core.Domain;
using AiLimit.Core.Storage;

namespace AiLimit.Tests;

public sealed class SnapshotStoreTests
{
    [Fact]
    public async Task LoadAsyncReturnsNullWhenFileIsMissing()
    {
        var directory = CreateTempDirectory();
        var store = new SnapshotStore(Path.Combine(directory, "snapshots.json"));

        var snapshot = await store.LoadAsync(CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task SaveAsyncPersistsSnapshot()
    {
        var directory = CreateTempDirectory();
        var store = new SnapshotStore(Path.Combine(directory, "snapshots.json"));
        var expected = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-05-17T10:00:00+09:00"),
            UsageSource.Mock,
            UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5-hour limit", 63, null, "soon", "high")]);

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal(expected.ProviderId, actual.ProviderId);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.CheckedAt, actual.CheckedAt);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Collection(
            actual.Windows,
            window =>
            {
                Assert.Equal("five-hour", window.Id);
                Assert.Equal("5-hour limit", window.Label);
                Assert.Equal(63, window.PercentRemaining);
                Assert.Equal("soon", window.ResetLabel);
                Assert.Equal("high", window.Confidence);
            });
    }

    [Fact]
    public async Task SaveAllAsyncPersistsEverySnapshot()
    {
        var directory = CreateTempDirectory();
        var store = new SnapshotStore(Path.Combine(directory, "snapshots.json"));
        var snapshots = new[]
        {
            Snapshot("codex", "ChatGPT Codex"),
            Snapshot("gemini-pro", "Google Antigravity")
        };

        await store.SaveAllAsync(snapshots, CancellationToken.None);
        var actual = await store.LoadAllAsync(CancellationToken.None);

        Assert.Collection(
            actual,
            snapshot => Assert.Equal("codex", snapshot.ProviderId),
            snapshot => Assert.Equal("gemini-pro", snapshot.ProviderId));
    }

    [Fact]
    public async Task LoadAllAsyncReadsLegacySingleSnapshotFile()
    {
        var directory = CreateTempDirectory();
        var store = new SnapshotStore(Path.Combine(directory, "snapshots.json"));
        var expected = Snapshot("codex", "ChatGPT Codex");

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAllAsync(CancellationToken.None);

        var snapshot = Assert.Single(actual);
        Assert.Equal(expected.ProviderId, snapshot.ProviderId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    public async Task LoadAllAsyncReturnsEmptyWhenSnapshotFileIsEmptyOrCorrupt(string contents)
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "snapshots.json");
        await File.WriteAllTextAsync(path, contents);
        var store = new SnapshotStore(path);

        var snapshots = await store.LoadAllAsync(CancellationToken.None);

        Assert.Empty(snapshots);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AiLimit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static UsageSnapshot Snapshot(string providerId, string displayName)
    {
        return new UsageSnapshot(
            providerId,
            displayName,
            DateTimeOffset.Parse("2026-05-17T10:00:00+09:00"),
            UsageSource.Mock,
            UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5-hour limit", 63, null, "soon", "high")]);
    }
}
