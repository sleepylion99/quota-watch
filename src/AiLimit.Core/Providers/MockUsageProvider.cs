using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers;

public sealed class MockUsageProvider : IUsageProvider
{
    private readonly int _primaryPercent;
    private readonly int _weeklyPercent;
    private readonly UsageSource _source;

    public MockUsageProvider()
        : this("codex", "ChatGPT Codex", 63, 41, UsageSource.Mock)
    {
    }

    public MockUsageProvider(
        string id,
        string displayName,
        int primaryPercent,
        int weeklyPercent,
        UsageSource source = UsageSource.Mock)
    {
        Descriptor = new ProviderDescriptor(id, displayName, true);
        _primaryPercent = primaryPercent;
        _weeklyPercent = weeklyPercent;
        _source = source;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        IReadOnlyList<UsageWindow> windows =
        [
            new(
                "five-hour",
                "5-hour limit",
                _primaryPercent,
                now.AddHours(2).AddMinutes(18),
                null,
                "high"),
            new(
                "weekly",
                "Weekly limit",
                _weeklyPercent,
                now.Date.AddDays(2).AddHours(9),
                null,
                "medium")
        ];

        var snapshot = new UsageSnapshot(
            Descriptor.Id,
            Descriptor.DisplayName,
            now,
            _source,
            UsageStatus.Fresh,
            windows);

        return Task.FromResult(snapshot);
    }
}
