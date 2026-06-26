using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// Polls every non-active account across the given providers and returns their full usage snapshots.
/// Per-account failures are swallowed (a depleted token must not break the others). Pure and timer-free
/// so the App layer owns scheduling and the toggle gate.
/// </summary>
public sealed class InactiveAccountUsageCollector
{
    private readonly IReadOnlyList<IUsageAccountProvider> _providers;
    private readonly Action<string>? _log;

    public InactiveAccountUsageCollector(IReadOnlyList<IUsageAccountProvider> providers, Action<string>? log = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _log = log;
    }

    public async Task<IReadOnlyList<UsageSnapshot>> CollectAsync(CancellationToken cancellationToken)
    {
        var results = new List<UsageSnapshot>();
        foreach (var provider in _providers)
        {
            var activeId = provider.GetActiveId();
            foreach (var record in provider.LoadAccounts())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (activeId is { } id && record.Id == id)
                {
                    continue; // active account already covered by the normal refresh loop
                }

                try
                {
                    var snapshot = await provider.PollUsageAsync(record, cancellationToken).ConfigureAwait(false);
                    if (snapshot.Status == UsageStatus.Fresh)
                    {
                        results.Add(snapshot);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Inactive poll failed for {provider.ProviderKey}/{record.DisplayName}: {ex.Message}");
                }
            }
        }

        return results;
    }
}
