using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Refresh;

namespace AiLimit.Tests;

public sealed class UsageRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsyncReturnsMockProviderWindows()
    {
        var provider = new MockUsageProvider();
        var service = new UsageRefreshService();

        var result = await service.RefreshAsync(provider, CancellationToken.None);

        Assert.Equal("codex", result.ProviderId);
        Assert.Equal(UsageStatus.Fresh, result.Status);
        Assert.Collection(
            result.Windows,
            window => Assert.Equal("five-hour", window.Id),
            window => Assert.Equal("weekly", window.Id));
    }

    [Fact]
    public async Task RefreshAsyncConvertsProviderFailureToFailedSnapshot()
    {
        var provider = new ThrowingUsageProvider();
        var service = new UsageRefreshService();

        var result = await service.RefreshAsync(provider, CancellationToken.None);

        Assert.Equal("broken", result.ProviderId);
        Assert.Equal(UsageStatus.Failed, result.Status);
        Assert.Equal("provider exploded", result.LastError);
        Assert.Empty(result.Windows);
    }

    [Fact]
    public async Task RefreshAllAsyncReturnsEveryProviderSnapshot()
    {
        IUsageProvider[] providers =
        [
            new MockUsageProvider("codex", "ChatGPT Codex", 63, 41),
            new MockUsageProvider("claude", "Claude Sonnet", 72, 54),
            new MockUsageProvider("gemini", "Gemini Pro", 88, 67)
        ];
        var service = new UsageRefreshService();

        var result = await service.RefreshAllAsync(providers, CancellationToken.None);

        Assert.Collection(
            result,
            snapshot => Assert.Equal("codex", snapshot.ProviderId),
            snapshot => Assert.Equal("claude", snapshot.ProviderId),
            snapshot => Assert.Equal("gemini", snapshot.ProviderId));
    }

    [Fact]
    public async Task RefreshAllAsyncRefreshesProvidersInParallel()
    {
        IUsageProvider[] providers =
        [
            new DelayedUsageProvider("one", TimeSpan.FromMilliseconds(250)),
            new DelayedUsageProvider("two", TimeSpan.FromMilliseconds(250)),
            new DelayedUsageProvider("three", TimeSpan.FromMilliseconds(250))
        ];
        var service = new UsageRefreshService();
        var startedAt = DateTimeOffset.UtcNow;

        var result = await service.RefreshAllAsync(providers, CancellationToken.None);

        Assert.Equal(["one", "two", "three"], result.Select(snapshot => snapshot.ProviderId));
        Assert.True(
            DateTimeOffset.UtcNow - startedAt < TimeSpan.FromMilliseconds(600),
            "RefreshAllAsync should wait for the slowest provider, not the sum of every provider.");
    }

    [Fact]
    public async Task RefreshAsyncTimesOutSlowProvider()
    {
        var provider = new DelayedUsageProvider("slow", TimeSpan.FromSeconds(3));
        var service = new UsageRefreshService(TimeSpan.FromMilliseconds(100));
        var startedAt = DateTimeOffset.UtcNow;

        var result = await service.RefreshAsync(provider, CancellationToken.None);

        Assert.Equal("slow", result.ProviderId);
        Assert.Equal(UsageStatus.Failed, result.Status);
        Assert.Contains("timed out", result.LastError);
        Assert.True(DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RefreshAsyncAllowsAntigravityToOutliveDefaultProviderTimeout()
    {
        var provider = new DelayedUsageProvider("gemini-pro", TimeSpan.FromMilliseconds(200));
        var service = new UsageRefreshService(TimeSpan.FromMilliseconds(50));

        var result = await service.RefreshAsync(provider, CancellationToken.None);

        Assert.Equal(UsageStatus.Fresh, result.Status);
        Assert.Single(result.Windows);
    }

    private sealed class ThrowingUsageProvider : IUsageProvider
    {
        public ProviderDescriptor Descriptor { get; } = new("broken", "Broken Provider", true);

        public Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("provider exploded");
        }
    }

    private sealed class DelayedUsageProvider(string id, TimeSpan delay) : IUsageProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(id, id, true);

        public async Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new UsageSnapshot(
                id,
                id,
                DateTimeOffset.Now,
                UsageSource.Mock,
                UsageStatus.Fresh,
                [new UsageWindow("five-hour", "5-hour limit", 90, null, null, "high")]);
        }
    }
}
