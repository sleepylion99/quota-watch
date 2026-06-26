namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// A parameterized stub implementation of <see cref="IAccountProvider"/> for providers
/// that are not yet wired (e.g. Codex, Claude). Lets tabs render with empty state
/// while the actual backend wiring lands in follow-up plans.
/// </summary>
public sealed class StubAccountProvider : IAccountProvider, ITrashableAccountProvider
{
    public StubAccountProvider(string providerKey, string displayName)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(providerKey));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(displayName));

        ProviderKey = providerKey;
        DisplayName = displayName;
    }

    public string ProviderKey { get; }
    public string DisplayName { get; }

    public IReadOnlyList<AccountRecord> LoadAccounts() => [];

    public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(PollAsync)} is unreachable for stub provider '{ProviderKey}'.");

    public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException($"{DisplayName} account import is not yet implemented.");

    public void Remove(Guid id) { }

    public void MarkActive(Guid? id) { }

    public Guid? GetActiveId() => null;

    public bool CanTrash(AccountRecord account) => false;

    public IReadOnlyList<TrashedAccountRecord> LoadTrash() => [];

    public Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
}
