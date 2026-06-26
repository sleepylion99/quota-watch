namespace AiLimit.Core.Providers.Accounts;

public interface IAccountProvider
{
    string ProviderKey { get; }
    string DisplayName { get; }

    IReadOnlyList<AccountRecord> LoadAccounts();
    Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken cancellationToken);
    Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken cancellationToken);
    void Remove(Guid id);
    /// <summary>Sets which stored account is the active selection. Pass <c>null</c> to clear the override and fall back to the provider's local source.</summary>
    void MarkActive(Guid? id);
    Guid? GetActiveId();
}
