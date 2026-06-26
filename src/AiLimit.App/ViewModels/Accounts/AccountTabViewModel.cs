using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiLimit.App.Localization;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;

namespace AiLimit.App.ViewModels.Accounts;

public sealed class AccountTabViewModel : INotifyPropertyChanged
{
    private readonly IAccountProvider _provider;
    private readonly AppLanguage _language;
    private readonly ICodexProfileCreator? _profileCreator;
    private readonly Func<CancellationToken, Task<AntigravityLoginResult>>? _signIn;
    private readonly ITrashableAccountProvider? _trashable;
    private readonly bool _usesUsedPercent;
    private string? _syncErrorMessage;
    private CancellationTokenSource? _bannerDismissCts;

    public AccountTabViewModel(IAccountProvider provider, AppLanguage language = AppLanguage.English,
        Func<CancellationToken, Task<AntigravityLoginResult>>? signIn = null,
        AntigravityOAuthClientPanelViewModel? oauthClientPanel = null,
        ClaudeSignInViewModel? claudeSignIn = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _language = language;
        _signIn = signIn;
        ClaudeSignIn = claudeSignIn;
        ClaudeSignIn?.SetLanguage(language);
        _trashable = provider as ITrashableAccountProvider;
        // Claude and Antigravity report "used" semantics; Codex reports "remaining".
        _usesUsedPercent = provider.ProviderKey is "gemini-pro" or "claude";
        OAuthClientPanel = oauthClientPanel;
        ProviderKey = provider.ProviderKey;
        TabHeader = provider.DisplayName;

        var syncKey = provider.ProviderKey == "gemini-pro"
            ? AppStringKeys.AccountsSyncFromIde
            : AppStringKeys.AccountsSyncFromCli;
        SyncButtonText = AppText.Get(language, syncKey);
        RefreshAllButtonText = AppText.Get(language, AppStringKeys.AccountsRefreshAll);
        SwitchButtonText = AppText.Get(language, AppStringKeys.AccountsSwitch);
        TrashButtonText = AppText.Get(language, AppStringKeys.AccountsMoveToTrash);
        ColumnAccountText = AppText.Get(language, AppStringKeys.AccountsColumnAccount);
        ColumnStatusText = AppText.Get(language, AppStringKeys.AccountsColumnStatus);
        ColumnPlanText = AppText.Get(language, AppStringKeys.AccountsColumnPlan);
        EmptyStateText = AppText.Get(language, AppStringKeys.AccountsEmpty);
        ActiveBadgeText = AppText.Get(language, AppStringKeys.AccountsActiveBadge);

        _profileCreator = provider as ICodexProfileCreator;
        SupportsProfileCreation = _profileCreator is not null;
        PrimaryActionText = SupportsProfileCreation
            ? AppText.Get(language, AppStringKeys.AccountsAddProfile)
            : SyncButtonText;

        OAuthClientSectionTitleText = AppText.Get(language, AppStringKeys.AccountsOAuthClientSectionTitle);
        OAuthClientActiveLabelFormat = AppText.Get(language, AppStringKeys.AccountsOAuthClientActiveLabel);
        OAuthClientAddText = AppText.Get(language, AppStringKeys.AccountsOAuthClientAdd);
        OAuthClientLabelHintText = AppText.Get(language, AppStringKeys.AccountsOAuthClientLabelHint);
        OAuthClientIdHintText = AppText.Get(language, AppStringKeys.AccountsOAuthClientIdHint);
        OAuthClientSecretHintText = AppText.Get(language, AppStringKeys.AccountsOAuthClientSecretHint);
        OAuthClientRemoveText = AppText.Get(language, AppStringKeys.AccountsOAuthClientRemove);
        OAuthClientBuiltInText = AppText.Get(language, AppStringKeys.AccountsOAuthClientBuiltIn);
        SignInWithGoogleText = AppText.Get(language, AppStringKeys.AccountsSignInWithGoogle);
        SignInWithClaudeText = AppText.Get(language, AppStringKeys.AccountsSignInWithClaude);
        ClaudeSubmitCodeText = AppText.Get(language, AppStringKeys.AccountsSubmitClaudeCode);

        SupportsLocalIde = provider is ILocalIdeAccount;
        LocalIdeRowText = AppText.Get(language, AppStringKeys.AccountsLocalIdeRow);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderKey { get; }
    public string TabHeader { get; }
    public string SyncButtonText { get; }
    public string RefreshAllButtonText { get; }
    public string SwitchButtonText { get; }
    public string TrashButtonText { get; }
    public string ColumnAccountText { get; }
    public string ColumnStatusText { get; }
    public string ColumnPlanText { get; }
    public string EmptyStateText { get; }
    public string ActiveBadgeText { get; }
    public bool SupportsProfileCreation { get; }
    public bool SupportsLocalIde { get; }
    public string LocalIdeRowText { get; }
    public bool SupportsGoogleSignIn => _signIn is not null;
    public ClaudeSignInViewModel? ClaudeSignIn { get; }
    public bool SupportsClaudeSignIn => ClaudeSignIn is not null;
    public string SignInWithClaudeText { get; }
    public string ClaudeSubmitCodeText { get; }
    public string PrimaryActionText { get; }
    public AntigravityOAuthClientPanelViewModel? OAuthClientPanel { get; }
    public string OAuthClientSectionTitleText { get; }
    public string OAuthClientActiveLabelFormat { get; }
    public string OAuthClientAddText { get; }
    public string OAuthClientLabelHintText { get; }
    public string OAuthClientIdHintText { get; }
    public string OAuthClientSecretHintText { get; }
    public string OAuthClientRemoveText { get; }
    public string OAuthClientBuiltInText { get; }
    public string SignInWithGoogleText { get; }
    public ObservableCollection<AccountRowViewModel> Rows { get; } = new();
    public bool IsEmpty => Rows.Count == 0;

    public string? SyncErrorMessage
    {
        get => _syncErrorMessage;
        private set
        {
            if (SetField(ref _syncErrorMessage, value))
            {
                ScheduleBannerAutoDismiss(value);
            }
        }
    }

    /// <summary>How long a status banner stays before it auto-dismisses. Settable for tests.</summary>
    internal TimeSpan BannerAutoDismissDelay { get; set; } = TimeSpan.FromSeconds(4);

    private void ScheduleBannerAutoDismiss(string? message)
    {
        _bannerDismissCts?.Cancel();
        _bannerDismissCts = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _bannerDismissCts = cts;
        _ = DismissBannerAfterDelayAsync(cts);
    }

    private async Task DismissBannerAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(BannerAutoDismissDelay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            cts.Dispose();
            return;
        }

        // Only clear if this is still the latest scheduled dismissal (a newer message
        // replaces the timer) — and clearing routes back through the setter, which
        // cancels this now-completed CTS harmlessly.
        if (ReferenceEquals(_bannerDismissCts, cts))
        {
            SyncErrorMessage = null;
        }

        cts.Dispose();
    }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = _provider.LoadAccounts();
        var activeId = _provider.GetActiveId();

        Rows.Clear();
        if (SupportsLocalIde)
        {
            var localRow = new AccountRowViewModel(
                new AccountRecord(Guid.Empty, _provider.ProviderKey, LocalIdeRowText, null, DateTimeOffset.UtcNow),
                _language,
                usesUsedPercent: _usesUsedPercent)
            {
                IsActive = activeId is null
            };
            Rows.Add(localRow);
        }

        foreach (var record in records)
        {
            var row = new AccountRowViewModel(
                record,
                _language,
                _trashable?.CanTrash(record) == true,
                _usesUsedPercent);
            if (activeId.HasValue && record.Id == activeId.Value)
            {
                row.IsActive = true;
            }
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        return Task.CompletedTask;
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        var records = _provider.LoadAccounts();
        var byId = records.ToDictionary(r => r.Id);

        foreach (var row in Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(row.Id, out var record))
            {
                continue;
            }

            try
            {
                var snapshot = await _provider.PollAsync(record, cancellationToken);
                row.ApplySnapshot(snapshot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                row.ApplySnapshot(AccountSnapshot.Failure(ex.Message));
            }
        }

        // Polling may have resolved and cached an account name (e.g. Claude email); re-read the
        // records and update row names in place without discarding the snapshot just applied.
        var refreshed = _provider.LoadAccounts().ToDictionary(r => r.Id);
        foreach (var row in Rows)
        {
            if (refreshed.TryGetValue(row.Id, out var updated))
            {
                row.UpdateAccount(updated);
            }
        }
    }

    public async Task SyncFromLocalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var imported = await _provider.ImportFromLocalSourceAsync(cancellationToken);
            if (imported is null)
            {
                SyncErrorMessage = AppText.Get(_language, AppStringKeys.AccountsSyncFailedNoLocalToken);
                return;
            }

            SyncErrorMessage = null;
            await ReloadAsync(cancellationToken);
        }
        catch (NotImplementedException)
        {
            SyncErrorMessage = AppText.Get(_language, AppStringKeys.AccountsSyncNotYetImplemented, _provider.DisplayName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SyncErrorMessage = ex.Message;
        }
    }

    public Task SwitchToAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _provider.MarkActive(id == Guid.Empty ? (Guid?)null : id);

        foreach (var row in Rows)
        {
            row.IsActive = row.Id == id;
        }

        return Task.CompletedTask;
    }

    public async Task CreateProfileAsync(CancellationToken cancellationToken)
    {
        if (_profileCreator is null) { return; }
        try
        {
            var result = await _profileCreator.CreateParallelProfileAsync(cancellationToken).ConfigureAwait(false);
            SyncErrorMessage = result.Outcome switch
            {
                CreateProfileOutcome.NeedsElevation => AppText.Get(_language, AppStringKeys.AccountsCodexNeedsAdmin),
                CreateProfileOutcome.NoSourceProfile => AppText.Get(_language, AppStringKeys.AccountsCodexEmpty),
                CreateProfileOutcome.Created => AppText.Get(_language, AppStringKeys.AccountsCodexProfileCreated, result.LaunchCommand ?? ""),
                _ => result.ErrorMessage ?? "Failed to create profile."
            };
            if (result.Outcome == CreateProfileOutcome.Created)
            {
                await ReloadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { SyncErrorMessage = ex.Message; }
    }

    public async Task SignInWithGoogleAsync(CancellationToken cancellationToken)
    {
        if (_signIn is null) { return; }
        try
        {
            var result = await _signIn(cancellationToken);
            SyncErrorMessage = result.Outcome switch
            {
                AntigravityLoginOutcome.Added => AppText.Get(_language, AppStringKeys.AccountsLoginAdded, result.Email ?? ""),
                AntigravityLoginOutcome.Duplicate => AppText.Get(_language, AppStringKeys.AccountsLoginDuplicate),
                AntigravityLoginOutcome.NoActiveClient => AppText.Get(_language, AppStringKeys.AccountsLoginNoActiveClient),
                AntigravityLoginOutcome.Cancelled => AppText.Get(_language, AppStringKeys.AccountsLoginCancelled),
                AntigravityLoginOutcome.ExchangeFailed => AppText.Get(_language, AppStringKeys.AccountsLoginExchangeFailed, result.ErrorMessage ?? ""),
                AntigravityLoginOutcome.UserInfoFailed => AppText.Get(_language, AppStringKeys.AccountsLoginUserInfoFailed),
                _ => AppText.Get(_language, AppStringKeys.AccountsLoginAuthFailed, result.ErrorMessage ?? ""),
            };
            if (result.Outcome is AntigravityLoginOutcome.Added)
            {
                await ReloadAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { SyncErrorMessage = ex.Message; }
    }

    public async Task CompleteClaudeSignInAsync(CancellationToken cancellationToken)
    {
        if (ClaudeSignIn is null) { return; }

        await ClaudeSignIn.CompleteAsync(cancellationToken);
        if (!ClaudeSignIn.AwaitingCode)
        {
            await ReloadAsync(cancellationToken);
        }
    }

    public async Task RefreshAndRescanAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        await RefreshAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<TrashAccountRowViewModel> LoadTrashRows()
    {
        return _trashable?.LoadTrash()
            .Select(r => new TrashAccountRowViewModel(r, TabHeader, _language))
            .ToList()
            .AsReadOnly() ?? [];
    }

    public async Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_trashable is null) { return; }
        await _trashable.MoveToTrashAsync(id, cancellationToken);
        SyncErrorMessage = AppText.Get(_language, AppStringKeys.AccountsMovedToTrash);
        await ReloadAsync(cancellationToken);
    }

    public async Task RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_trashable is null) { return; }
        await _trashable.RestoreFromTrashAsync(id, cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_trashable is null) { return; }
        await _trashable.DeletePermanentlyAsync(id, cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
