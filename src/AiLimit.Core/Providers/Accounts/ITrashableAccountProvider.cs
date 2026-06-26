namespace AiLimit.Core.Providers.Accounts;

public sealed record TrashedAccountRecord(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    string? Email,
    DateTimeOffset DeletedAt,
    string? ResourcePath = null);

public interface ITrashableAccountProvider
{
    bool CanTrash(AccountRecord account);
    IReadOnlyList<TrashedAccountRecord> LoadTrash();
    Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken);
    Task RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken);
    Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken);
}
