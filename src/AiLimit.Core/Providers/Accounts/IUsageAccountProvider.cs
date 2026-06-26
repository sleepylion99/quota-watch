using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// An account provider that can return a full <see cref="UsageSnapshot"/> (windows + AccountKey) for a
/// specific account, suitable for limit-warning evaluation. The snapshot's DisplayName carries an account
/// label so warning toasts identify which account is depleted.
/// </summary>
public interface IUsageAccountProvider : IAccountProvider
{
    Task<UsageSnapshot> PollUsageAsync(AccountRecord record, CancellationToken cancellationToken);
}
