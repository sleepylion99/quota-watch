using AiLimit.Core.Domain;
using AiLimit.Core.Providers.Accounts;
using System.Net.Http;

namespace AiLimit.Tests.Accounts;

public sealed class InactiveAccountUsageCollectorTests
{
    [Fact]
    public async Task CollectAsyncPollsOnlyInactiveAccountsAndSwallowsFailures()
    {
        var active = new AccountRecord(Guid.NewGuid(), "claude", ".claude", null, DateTimeOffset.UtcNow);
        var inactive = new AccountRecord(Guid.NewGuid(), "claude", ".claude2", null, DateTimeOffset.UtcNow);
        var boom = new AccountRecord(Guid.NewGuid(), "claude", ".claude3", null, DateTimeOffset.UtcNow);

        var fake = new FakeUsageAccountProvider(
            accounts: [active, inactive, boom],
            activeId: active.Id,
            results: new() { [inactive.Id] = Fresh("claude", "acct-2") },
            throwFor: boom.Id);

        var collector = new InactiveAccountUsageCollector([fake]);

        var snapshots = await collector.CollectAsync(CancellationToken.None);

        Assert.Single(snapshots);
        Assert.Equal("acct-2", snapshots[0].AccountKey);
    }

    [Fact]
    public async Task CollectAsyncReturnsEmptyWhenNoProviders()
    {
        var collector = new InactiveAccountUsageCollector([]);
        var snapshots = await collector.CollectAsync(CancellationToken.None);
        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task CollectAsyncExcludesFailedStatusSnapshots()
    {
        var inactive = new AccountRecord(Guid.NewGuid(), "claude", ".claude2", null, DateTimeOffset.UtcNow);

        var failedSnapshot = new UsageSnapshot("claude", "claude", DateTimeOffset.UtcNow, UsageSource.Agent,
            UsageStatus.Failed, [], AccountKey: "acct-failed");

        var fake = new FakeUsageAccountProvider(
            accounts: [inactive],
            activeId: null,
            results: new() { [inactive.Id] = failedSnapshot });

        var collector = new InactiveAccountUsageCollector([fake]);
        var snapshots = await collector.CollectAsync(CancellationToken.None);

        Assert.Empty(snapshots);
    }

    private static UsageSnapshot Fresh(string providerId, string accountKey)
        => new(providerId, providerId, DateTimeOffset.UtcNow, UsageSource.Agent, UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5h", 95, null, null, "high", IsUsedPercent: true)], AccountKey: accountKey);

    private sealed class FakeUsageAccountProvider(
        IReadOnlyList<AccountRecord> accounts,
        Guid? activeId,
        Dictionary<Guid, UsageSnapshot> results,
        Guid? throwFor = null) : IUsageAccountProvider
    {
        public string ProviderKey => "claude";
        public string DisplayName => "Claude Code";
        public IReadOnlyList<AccountRecord> LoadAccounts() => accounts;
        public Guid? GetActiveId() => activeId;
        public Task<UsageSnapshot> PollUsageAsync(AccountRecord record, CancellationToken ct)
        {
            if (throwFor == record.Id) { throw new HttpRequestException("boom"); }
            return Task.FromResult(results[record.Id]);
        }
        public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken ct) => throw new NotSupportedException();
        public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken ct) => throw new NotSupportedException();
        public void Remove(Guid id) { }
        public void MarkActive(Guid? id) { }
    }
}
