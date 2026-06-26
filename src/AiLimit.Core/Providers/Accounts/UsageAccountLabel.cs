using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>Stamps a per-account label onto a usage snapshot's DisplayName so warning toasts say which account.</summary>
internal static class UsageAccountLabel
{
    public static UsageSnapshot Stamp(UsageSnapshot snapshot, string providerDisplayName, AccountRecord record)
        => snapshot with { DisplayName = $"{providerDisplayName} — {Label(record)}" };

    // AccountKey is left null on failure: a Failed snapshot is filtered out before warning evaluation
    // (which only considers Fresh snapshots), and a real key is a token hash — synthesizing a Guid-shaped
    // value here would only risk a format mismatch against the live key for the same account.
    public static UsageSnapshot Failed(string providerId, string providerDisplayName, AccountRecord record, string error)
        => new(providerId, $"{providerDisplayName} — {Label(record)}", DateTimeOffset.UtcNow,
            UsageSource.Agent, UsageStatus.Failed, [], LastError: error);

    private static string Label(AccountRecord record)
        => string.IsNullOrWhiteSpace(record.Email) ? record.DisplayName : record.Email;
}
