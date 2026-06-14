namespace AiLimit.Core.Domain;

public enum UsageStatus
{
    Fresh,
    Refreshing,
    Stale,
    Failed
}

public enum UsageSource
{
    Mock,
    Browser,
    Agent,
    Manual
}

public sealed record UsageWindow(
    string Id,
    string Label,
    int PercentRemaining,
    DateTimeOffset? ResetAt,
    string? ResetLabel,
    string Confidence,
    bool IsUsedPercent = false,
    double? PrecisePercent = null);

public sealed record UsageSnapshot(
    string ProviderId,
    string DisplayName,
    DateTimeOffset CheckedAt,
    UsageSource Source,
    UsageStatus Status,
    IReadOnlyList<UsageWindow> Windows,
    string? LastError = null,
    string? AccountKey = null,
    string? SourceChannel = null,
    string? CloudFailureSummary = null,
    string? IdeFailureSummary = null,
    string? OAuthClientOrigin = null);

public sealed record ProviderDescriptor(
    string Id,
    string DisplayName,
    bool IsEnabled);
