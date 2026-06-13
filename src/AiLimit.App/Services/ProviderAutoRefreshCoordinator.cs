using AiLimit.Core.Domain;
using AiLimit.Core.Providers;

namespace AiLimit.App.Services;

public sealed class ProviderAutoRefreshCoordinator
{
    private readonly Dictionary<string, ProviderAutoRefreshStatus> _statuses = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ProviderAutoRefreshStatus> Statuses => _statuses;

    public IUsageProvider? SelectNext(IReadOnlyList<IUsageProvider> providers, DateTimeOffset now)
    {
        return providers
            .Select((provider, index) => new
            {
                Provider = provider,
                Index = index,
                Status = GetStatus(provider.Descriptor.Id)
            })
            .Where(item => item.Status.NextAutomaticRefreshAt is null || item.Status.NextAutomaticRefreshAt <= now)
            .OrderBy(item => item.Status.LastAttemptAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Provider)
            .FirstOrDefault();
    }

    public void RecordResult(UsageSnapshot snapshot)
    {
        var previous = GetStatus(snapshot.ProviderId);
        var failureCount = snapshot.Status == UsageStatus.Failed
            ? previous.FailureCount + 1
            : 0;
        var nextAutomaticRefreshAt = snapshot.Status == UsageStatus.Failed
            ? snapshot.CheckedAt + BackoffFor(failureCount)
            : (DateTimeOffset?)null;

        _statuses[snapshot.ProviderId] = new ProviderAutoRefreshStatus(
            snapshot.ProviderId,
            snapshot.CheckedAt,
            failureCount,
            nextAutomaticRefreshAt);
    }

    public void RemoveMissingProviders(IEnumerable<string> activeProviderIds)
    {
        var active = activeProviderIds.ToHashSet(StringComparer.Ordinal);
        foreach (var providerId in _statuses.Keys.Where(providerId => !active.Contains(providerId)).ToList())
        {
            _statuses.Remove(providerId);
        }
    }

    private ProviderAutoRefreshStatus GetStatus(string providerId)
    {
        return _statuses.TryGetValue(providerId, out var status)
            ? status
            : new ProviderAutoRefreshStatus(providerId, null, 0, null);
    }

    private static TimeSpan BackoffFor(int failureCount)
    {
        return failureCount switch
        {
            <= 1 => TimeSpan.Zero,
            2 => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(30)
        };
    }
}

public sealed record ProviderAutoRefreshStatus(
    string ProviderId,
    DateTimeOffset? LastAttemptAt,
    int FailureCount,
    DateTimeOffset? NextAutomaticRefreshAt);
