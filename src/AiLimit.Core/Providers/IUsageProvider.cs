using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers;

public interface IUsageProvider
{
    ProviderDescriptor Descriptor { get; }

    Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken);
}
