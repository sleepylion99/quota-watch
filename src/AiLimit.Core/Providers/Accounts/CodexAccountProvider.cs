using System.Linq;
using AiLimit.Core.Domain;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// IAccountProvider for Codex. Each account is a CODEX_HOME profile directory
/// discovered by the scanner. Switch (MarkActive) is a non-destructive pointer
/// that only repoints the dashboard tile via ResolveActiveAuthPath(); it never
/// touches auth tokens. Polling is delegated so the App layer injects the real
/// CodexUsageProvider while tests stay offline.
/// </summary>
public sealed class CodexAccountProvider : IUsageAccountProvider, ICodexProfileCreator, ITrashableAccountProvider
{
    private static readonly TimeSpan DefaultUnclaimedProfileCleanupDelay = TimeSpan.FromSeconds(60);

    private readonly CodexProfileScanner _scanner;
    private readonly CodexActiveSelection _activeSelection;
    private readonly ICodexProfileCreator _linker;
    private readonly Func<string, CancellationToken, Task<AccountSnapshot>> _poll;
    private readonly AccountTrashStore _trashStore;
    private readonly Func<string, CancellationToken, Task<UsageSnapshot>>? _pollUsage;
    private readonly TimeSpan _unclaimedProfileCleanupDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Action? _onUnclaimedProfileCleanupComplete;

    public CodexAccountProvider(
        CodexProfileScanner scanner,
        CodexActiveSelection activeSelection,
        ICodexProfileCreator linker,
        Func<string, CancellationToken, Task<AccountSnapshot>> poll,
        AccountTrashStore? trashStore = null,
        Func<string, CancellationToken, Task<UsageSnapshot>>? pollUsage = null,
        TimeSpan? unclaimedProfileCleanupDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Action? onUnclaimedProfileCleanupComplete = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _activeSelection = activeSelection ?? throw new ArgumentNullException(nameof(activeSelection));
        _linker = linker ?? throw new ArgumentNullException(nameof(linker));
        _poll = poll ?? throw new ArgumentNullException(nameof(poll));
        _trashStore = trashStore ?? new AccountTrashStore(AccountTrashStore.DefaultPath());
        _pollUsage = pollUsage;
        _unclaimedProfileCleanupDelay = unclaimedProfileCleanupDelay ?? DefaultUnclaimedProfileCleanupDelay;
        _delayAsync = delayAsync ?? Task.Delay;
        _onUnclaimedProfileCleanupComplete = onUnclaimedProfileCleanupComplete;
    }

    public string ProviderKey => "codex";
    public string DisplayName => "ChatGPT Codex";

    public IReadOnlyList<AccountRecord> LoadAccounts() => _scanner.Scan()
        .Where(r => !_trashStore.Contains(ProviderKey, r.Id))
        .ToList()
        .AsReadOnly();

    public void Remove(Guid id) { /* per-row remove is out of scope */ }

    public void MarkActive(Guid? id) => _activeSelection.Set(id);

    public Guid? GetActiveId() => _activeSelection.Get();

    /// <summary>Auth.json path of the active profile, or the default .codex when none is selected / the selection is gone.</summary>
    public string? ResolveActiveAuthPath()
    {
        if (_activeSelection.Get() is { } id && _scanner.ResolveAuthPath(id) is { } path)
        {
            return path;
        }

        return Path.Combine(_scanner.HomeRoot, ".codex", "auth.json");
    }

    public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken cancellationToken)
    {
        var authPath = _scanner.ResolveAuthPath(record.Id);
        return authPath is null
            ? Task.FromResult(AccountSnapshot.Failure("Profile no longer exists."))
            : _poll(authPath, cancellationToken);
    }

    public async Task<UsageSnapshot> PollUsageAsync(AccountRecord record, CancellationToken cancellationToken)
    {
        if (_pollUsage is null)
        {
            return UsageAccountLabel.Failed(ProviderKey, DisplayName, record, "Usage polling is not configured.");
        }

        var authPath = _scanner.ResolveAuthPath(record.Id);
        if (authPath is null)
        {
            return UsageAccountLabel.Failed(ProviderKey, DisplayName, record, "Profile no longer exists.");
        }

        var snapshot = await _pollUsage(authPath, cancellationToken).ConfigureAwait(false);
        return UsageAccountLabel.Stamp(snapshot, DisplayName, record);
    }

    /// <summary>Codex has no remote source to import; returns the default .codex profile (or the first found) so Refresh can re-scan.</summary>
    public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken cancellationToken)
    {
        var records = _scanner.Scan();
        return Task.FromResult(records.FirstOrDefault(r => r.DisplayName == ".codex") ?? records.FirstOrDefault());
    }

    public async Task<CreateProfileResult> CreateParallelProfileAsync(CancellationToken cancellationToken)
    {
        var result = await _linker.CreateParallelProfileAsync(cancellationToken).ConfigureAwait(false);
        if (result.Outcome == CreateProfileOutcome.Created
            && !string.IsNullOrWhiteSpace(result.ProfilePath))
        {
            _ = CleanupUnclaimedProfileAsync(result.ProfilePath, CancellationToken.None);
        }

        return result;
    }

    public bool CanTrash(AccountRecord account)
    {
        if (!string.Equals(account.ProviderKey, ProviderKey, StringComparison.Ordinal))
        {
            return false;
        }

        var path = _scanner.ResolveProfilePath(account.Id);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return IsSafeNumberedProfile(path);
    }

    public IReadOnlyList<TrashedAccountRecord> LoadTrash() => _trashStore.List(ProviderKey);

    public Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = _scanner.Scan().FirstOrDefault(r => r.Id == id);
        if (record is null || !CanTrash(record))
        {
            return Task.CompletedTask;
        }

        _trashStore.Put(new TrashedAccountRecord(
            record.Id,
            ProviderKey,
            record.DisplayName,
            record.Email,
            DateTimeOffset.UtcNow,
            _scanner.ResolveProfilePath(record.Id)));
        if (_activeSelection.Get() == id)
        {
            _activeSelection.Set(null);
        }

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
        if (trashed is null)
        {
            return Task.CompletedTask;
        }

        var path = _scanner.ResolveProfilePath(id) ?? trashed.ResourcePath;
        if (string.IsNullOrWhiteSpace(path) || !IsSafeNumberedProfile(path))
        {
            throw new InvalidOperationException("Codex profile path is not safe to delete.");
        }

        if (Directory.Exists(path))
        {
            DeleteProfileDirectory(path);
        }

        _trashStore.Remove(ProviderKey, id);
        return Task.CompletedTask;
    }

    private static void DeleteProfileDirectory(string profilePath)
    {
        var root = new DirectoryInfo(profilePath);
        if (!root.Exists)
        {
            return;
        }

        DeleteDirectory(root);
    }

    private static void DeleteDirectory(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if ((child.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                child.Delete();
                continue;
            }

            DeleteDirectory(child);
        }

        directory.Attributes = FileAttributes.Directory;
        directory.Delete();
    }

    private async Task CleanupUnclaimedProfileAsync(string profilePath, CancellationToken cancellationToken)
    {
        try
        {
            await _delayAsync(_unclaimedProfileCleanupDelay, cancellationToken).ConfigureAwait(false);
            if (!IsSafeNumberedProfile(profilePath))
            {
                return;
            }

            if (File.Exists(Path.Combine(profilePath, "auth.json")))
            {
                return;
            }

            if (Directory.Exists(profilePath))
            {
                DeleteProfileDirectory(profilePath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Best-effort cleanup only; profile creation must not fail after returning success.
        }
        finally
        {
            _onUnclaimedProfileCleanupComplete?.Invoke();
        }
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
        if (!name.StartsWith(".codex", StringComparison.Ordinal) || name.Length <= ".codex".Length)
        {
            return false;
        }

        return int.TryParse(name[".codex".Length..], out var number) && number >= 2;
    }
}
