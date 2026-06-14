using System.Text.Json;
using AiLimit.Core.Domain;

namespace AiLimit.Core.Storage;

public sealed class UsageHistoryStore
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(12);
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);
    public const int MaximumSamplesPerWindow = 240;

    private readonly JsonFileStore<IReadOnlyList<UsageSample>> _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UsageHistoryStore(string path, Func<DateTimeOffset>? now = null)
    {
        _store = new JsonFileStore<IReadOnlyList<UsageSample>>(path);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<UsageSample>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _store.LoadAsync(cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<UsageSample>> AppendAsync(
        IReadOnlyList<UsageSample> samples,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var cutoff = _now() - Retention;
            var retained = existing
                .Concat(samples)
                .Where(sample => sample.AtUtc >= cutoff)
                .GroupBy(sample => new
                {
                    sample.ProviderId,
                    sample.WindowId,
                    sample.AccountKey
                })
                .SelectMany(group => Deduplicate(group.OrderBy(sample => sample.AtUtc))
                    .TakeLast(MaximumSamplesPerWindow))
                .OrderBy(sample => sample.AtUtc)
                .ToList();

            await _store.SaveAsync(retained, cancellationToken).ConfigureAwait(false);
            return retained;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IEnumerable<UsageSample> Deduplicate(IEnumerable<UsageSample> samples)
    {
        UsageSample? previous = null;
        foreach (var sample in samples)
        {
            if (previous is not null
                && sample.ConsumedPercent.Equals(previous.ConsumedPercent)
                && sample.AtUtc - previous.AtUtc < DuplicateWindow)
            {
                continue;
            }

            previous = sample;
            yield return sample;
        }
    }
}
