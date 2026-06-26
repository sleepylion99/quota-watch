using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// IAccountProvider for Claude. Each account is a .claude/.claudeN profile directory discovered by the
/// scanner. Switch (MarkActive) is a non-destructive pointer; it never rewrites credential files.
/// Polling is delegated so the App layer injects the real usage read. Only numbered profiles
/// (.claudeN, N>=2) can be trashed/deleted — the primary .claude is protected.
/// </summary>
public sealed class ClaudeAccountProvider : IUsageAccountProvider, ITrashableAccountProvider
{
    private readonly ClaudeProfileScanner _scanner;
    private readonly ClaudeActiveSelection _activeSelection;
    private readonly Func<string, CancellationToken, Task<AccountSnapshot>> _poll;
    private readonly AccountTrashStore _trashStore;
    private readonly Func<string, CancellationToken, Task<UsageSnapshot>>? _pollUsage;

    public ClaudeAccountProvider(
        ClaudeProfileScanner scanner,
        ClaudeActiveSelection activeSelection,
        Func<string, CancellationToken, Task<AccountSnapshot>> poll,
        AccountTrashStore? trashStore = null,
        Func<string, CancellationToken, Task<UsageSnapshot>>? pollUsage = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _activeSelection = activeSelection ?? throw new ArgumentNullException(nameof(activeSelection));
        _poll = poll ?? throw new ArgumentNullException(nameof(poll));
        _trashStore = trashStore ?? new AccountTrashStore(AccountTrashStore.DefaultPath());
        _pollUsage = pollUsage;
    }

    public string ProviderKey => "claude";
    public string DisplayName => "Claude Code";

    public IReadOnlyList<AccountRecord> LoadAccounts() => _scanner.Scan()
        .Where(r => !_trashStore.Contains(ProviderKey, r.Id))
        .ToList()
        .AsReadOnly();

    public void Remove(Guid id) { /* per-row remove is out of scope */ }

    public void MarkActive(Guid? id) => _activeSelection.Set(id);

    public Guid? GetActiveId() => _activeSelection.Get();

    public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken cancellationToken)
    {
        var credPath = _scanner.ResolveCredentialsPath(record.Id);
        return credPath is null
            ? Task.FromResult(AccountSnapshot.Failure("Profile no longer exists."))
            : _poll(credPath, cancellationToken);
    }

    public async Task<UsageSnapshot> PollUsageAsync(AccountRecord record, CancellationToken cancellationToken)
    {
        if (_pollUsage is null)
        {
            return UsageAccountLabel.Failed(ProviderKey, DisplayName, record, "Usage polling is not configured.");
        }

        var credPath = _scanner.ResolveCredentialsPath(record.Id);
        if (credPath is null)
        {
            return UsageAccountLabel.Failed(ProviderKey, DisplayName, record, "Profile no longer exists.");
        }

        var snapshot = await _pollUsage(credPath, cancellationToken).ConfigureAwait(false);
        return UsageAccountLabel.Stamp(snapshot, DisplayName, record);
    }

    /// <summary>Returns the default ~/.claude profile (or the first found) so Refresh can re-scan.</summary>
    public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken cancellationToken)
    {
        var records = _scanner.Scan();
        return Task.FromResult(records.FirstOrDefault(r => r.DisplayName == ".claude") ?? records.FirstOrDefault());
    }

    public bool CanTrash(AccountRecord account)
    {
        if (!string.Equals(account.ProviderKey, ProviderKey, StringComparison.Ordinal)) { return false; }
        var path = _scanner.ResolveProfilePath(account.Id);
        return !string.IsNullOrWhiteSpace(path) && IsSafeNumberedProfile(path);
    }

    public IReadOnlyList<TrashedAccountRecord> LoadTrash() => _trashStore.List(ProviderKey);

    public Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = _scanner.Scan().FirstOrDefault(r => r.Id == id);
        if (record is null || !CanTrash(record)) { return Task.CompletedTask; }

        _trashStore.Put(new TrashedAccountRecord(
            record.Id,
            ProviderKey,
            record.DisplayName,
            record.Email,
            DateTimeOffset.UtcNow,
            _scanner.ResolveProfilePath(record.Id)));
        if (_activeSelection.Get() == id) { _activeSelection.Set(null); }
        return Task.CompletedTask;
    }

    public Task RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _trashStore.Remove(ProviderKey, id);
        return Task.CompletedTask;
    }

    public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trashed = _trashStore.List(ProviderKey).FirstOrDefault(i => i.Id == id);
        if (trashed is null) { return Task.CompletedTask; }

        var path = _scanner.ResolveProfilePath(id) ?? trashed.ResourcePath;
        if (string.IsNullOrWhiteSpace(path) || !IsSafeNumberedProfile(path))
        {
            throw new InvalidOperationException("Claude profile path is not safe to delete.");
        }

        if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); }
        _trashStore.Remove(ProviderKey, id);
        return Task.CompletedTask;
    }

    private bool IsSafeNumberedProfile(string profilePath)
    {
        var fullRoot = Path.GetFullPath(_scanner.HomeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(profilePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(fullPath), fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileName(fullPath);
        if (!name.StartsWith(".claude", StringComparison.Ordinal) || name.Length <= ".claude".Length)
        {
            return false;
        }

        return int.TryParse(name[".claude".Length..], out var number) && number >= 2;
    }
}
