namespace AiLimit.Core.Domain;

public sealed record UsageSample(
    string ProviderId,
    string WindowId,
    DateTimeOffset AtUtc,
    double ConsumedPercent,
    string? AccountKey = null);
