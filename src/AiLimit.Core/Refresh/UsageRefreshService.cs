using AiLimit.Core.Domain;
using AiLimit.Core.Providers;

namespace AiLimit.Core.Refresh;

public sealed class UsageRefreshService
{
    private readonly TimeSpan _providerTimeout;

    public UsageRefreshService()
        : this(TimeSpan.FromSeconds(10))
    {
    }

    public UsageRefreshService(TimeSpan providerTimeout)
    {
        _providerTimeout = providerTimeout;
    }

    public async Task<IReadOnlyList<UsageSnapshot>> RefreshAllAsync(
        IEnumerable<IUsageProvider> providers,
        CancellationToken cancellationToken)
    {
        var refreshTasks = providers
            .Select(provider => RefreshAsync(provider, cancellationToken))
            .ToArray();
        return await Task.WhenAll(refreshTasks).ConfigureAwait(false);
    }

    public async Task<UsageSnapshot> RefreshAsync(
        IUsageProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var providerTimeout = TimeoutFor(provider);
            timeout.CancelAfter(providerTimeout);
            return await provider.RefreshAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(
                provider,
                $"Usage refresh timed out after {TimeoutFor(provider).TotalSeconds:0.#} seconds.");
        }
        catch (Exception ex)
        {
            return Failed(provider, ex.Message);
        }
    }

    private TimeSpan TimeoutFor(IUsageProvider provider)
    {
        return provider.Descriptor.Id == "gemini-pro"
            ? TimeSpan.FromSeconds(Math.Max(_providerTimeout.TotalSeconds, 25))
            : _providerTimeout;
    }

    private static UsageSnapshot Failed(IUsageProvider provider, string message)
    {
        return new UsageSnapshot(
            provider.Descriptor.Id,
            provider.Descriptor.DisplayName,
            DateTimeOffset.Now,
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            message);
    }
}
