using AiLimit.Core.Domain;
using AiLimit.Core.Storage;

namespace AiLimit.Tests;

public sealed class UsageHistoryStoreTests
{
    [Fact]
    public async Task LoadAsyncReturnsEmptyForMissingOrCorruptFile()
    {
        var path = TempFile();
        var now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var store = new UsageHistoryStore(path, () => now);

        Assert.Empty(await store.LoadAsync(CancellationToken.None));

        await File.WriteAllTextAsync(path, "{not-json");
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsyncSuppressesSameValueWithinSixtySeconds()
    {
        var path = TempFile();
        var now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var store = new UsageHistoryStore(path, () => now);

        var saved = await store.AppendAsync(
            [
                Sample(now.AddSeconds(-50), 42),
                Sample(now, 42),
                Sample(now.AddSeconds(1), 43)
            ],
            CancellationToken.None);

        Assert.Equal(2, saved.Count);
        Assert.Equal([42, 43], saved.Select(sample => sample.ConsumedPercent));
    }

    [Fact]
    public async Task AppendAsyncPrunesOldAndExcessSamplesPerWindow()
    {
        var path = TempFile();
        var now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var store = new UsageHistoryStore(path, () => now);
        var samples = Enumerable.Range(0, 260)
            .Select(index => Sample(now.AddMinutes(-259 + index), index % 100))
            .Prepend(Sample(now.AddHours(-13), 1))
            .ToList();

        var saved = await store.AppendAsync(samples, CancellationToken.None);

        Assert.Equal(240, saved.Count);
        Assert.All(saved, sample => Assert.True(sample.AtUtc >= now.AddHours(-12)));
        Assert.Equal(now, saved[^1].AtUtc);
    }

    [Fact]
    public async Task AppendAsyncKeepsAccountsInSeparateHistoryBuckets()
    {
        var path = TempFile();
        var now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var store = new UsageHistoryStore(path, () => now);

        var saved = await store.AppendAsync(
            [
                Sample(now, 42, "account-a"),
                Sample(now, 42, "account-b")
            ],
            CancellationToken.None);

        Assert.Equal(2, saved.Count);
        Assert.Equal(["account-a", "account-b"], saved.Select(sample => sample.AccountKey).Order());
    }

    private static UsageSample Sample(DateTimeOffset at, double consumed, string? accountKey = null) =>
        new("claude", "five-hour", at, consumed, accountKey);

    private static string TempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quota-watch-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "history.json");
    }
}
