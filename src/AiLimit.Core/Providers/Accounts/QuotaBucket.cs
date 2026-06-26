namespace AiLimit.Core.Providers.Accounts;

public sealed record QuotaBucket(
    string ModelId,
    int PercentRemaining,
    DateTimeOffset? ResetAt);
