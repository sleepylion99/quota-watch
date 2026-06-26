namespace AiLimit.Core.Providers.Accounts;

public sealed record AccountRecord(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    string? Email,
    DateTimeOffset LastSyncedAt);
