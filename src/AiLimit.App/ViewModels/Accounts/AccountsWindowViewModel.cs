using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using AiLimit.App.Localization;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;

namespace AiLimit.App.ViewModels.Accounts;

public sealed class AccountsWindowViewModel : INotifyPropertyChanged
{
    private int _selectedTabIndex;
    private bool _isTrashMode;
    private readonly AppLanguage _language;

    public AccountsWindowViewModel(
        IReadOnlyList<IAccountProvider> providers,
        AppLanguage language = AppLanguage.English,
        Func<CancellationToken, Task<AntigravityLoginResult>>? antigravitySignIn = null,
        AntigravityOAuthClientPanelViewModel? antigravityOAuthClientPanel = null,
        ClaudeSignInViewModel? claudeSignIn = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _language = language;
        Tabs = providers.Select(p =>
        {
            if (p.ProviderKey == "gemini-pro")
            {
                return new AccountTabViewModel(p, language, antigravitySignIn, antigravityOAuthClientPanel);
            }
            if (p.ProviderKey == "claude")
            {
                return new AccountTabViewModel(p, language, claudeSignIn: claudeSignIn);
            }
            return new AccountTabViewModel(p, language);
        }).ToList();
        _selectedTabIndex = ComputeDefaultSelectedTabIndex(Tabs);
        WindowTitle = AppText.Get(language, AppStringKeys.AccountsWindowTitle);
        TrashButtonText = AppText.Get(language, AppStringKeys.AccountsTrash);
        BackToAccountsText = AppText.Get(language, AppStringKeys.AccountsTrashBack);
        RestoreText = AppText.Get(language, AppStringKeys.AccountsTrashRestore);
        DeletePermanentlyText = AppText.Get(language, AppStringKeys.AccountsTrashDeletePermanently);
        CancelText = AppText.Get(language, AppStringKeys.AccountsTrashCancel);
        TrashEmptyText = AppText.Get(language, AppStringKeys.AccountsTrashEmpty);
        PermanentDeleteTitleText = AppText.Get(language, AppStringKeys.AccountsTrashConfirmTitle);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ActiveAccountChanged;

    public string WindowTitle { get; }
    public IReadOnlyList<AccountTabViewModel> Tabs { get; }
    public ObservableCollection<TrashAccountRowViewModel> TrashRows { get; } = new();
    public string TrashButtonText { get; }
    public string BackToAccountsText { get; }
    public string RestoreText { get; }
    public string DeletePermanentlyText { get; }
    public string CancelText { get; }
    public string TrashEmptyText { get; }
    public string PermanentDeleteTitleText { get; }
    public int TrashCount => Tabs.Sum(t => t.LoadTrashRows().Count);
    public string TrashButtonLabel => TrashCount == 0 ? TrashButtonText : $"{TrashButtonText} ({TrashCount})";

    public bool IsTrashMode
    {
        get => _isTrashMode;
        private set => SetField(ref _isTrashMode, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public AccountTabViewModel? SelectedTab
        => _selectedTabIndex >= 0 && _selectedTabIndex < Tabs.Count ? Tabs[_selectedTabIndex] : null;

    public Task ReloadCurrentTabAsync(CancellationToken cancellationToken)
    {
        var tab = SelectedTab;
        return tab is null ? Task.CompletedTask : tab.ReloadAsync(cancellationToken);
    }

    public async Task SwitchInCurrentTabAsync(Guid id, CancellationToken cancellationToken)
    {
        var tab = SelectedTab;
        if (tab is null)
        {
            return;
        }

        await tab.SwitchToAsync(id, cancellationToken);
        ActiveAccountChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task MoveToTrashInCurrentTabAsync(Guid id, CancellationToken cancellationToken)
    {
        var tab = SelectedTab;
        if (tab is null) { return; }
        await tab.MoveToTrashAsync(id, cancellationToken);
        ActiveAccountChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(TrashCount));
        OnPropertyChanged(nameof(TrashButtonLabel));
    }

    public Task OpenTrashAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReloadTrashRows();
        IsTrashMode = true;
        return Task.CompletedTask;
    }

    public Task CloseTrashAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsTrashMode = false;
        return Task.CompletedTask;
    }

    public async Task RestoreTrashAsync(TrashAccountRowViewModel row, CancellationToken cancellationToken)
    {
        var tab = Tabs.FirstOrDefault(t => t.ProviderKey == row.ProviderKey);
        if (tab is null) { return; }
        await tab.RestoreFromTrashAsync(row.Id, cancellationToken);
        ReloadTrashRows();
        ActiveAccountChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteTrashPermanentlyAsync(TrashAccountRowViewModel row, CancellationToken cancellationToken)
    {
        var tab = Tabs.FirstOrDefault(t => t.ProviderKey == row.ProviderKey);
        if (tab is null) { return; }
        await tab.DeletePermanentlyAsync(row.Id, cancellationToken);
        ReloadTrashRows();
        ActiveAccountChanged?.Invoke(this, EventArgs.Empty);
    }

    public string FormatPermanentDeleteMessage(TrashAccountRowViewModel row)
        => AppText.Get(_language, AppStringKeys.AccountsTrashConfirmMessage, row.AccountText, row.ProviderText);

    private void ReloadTrashRows()
    {
        TrashRows.Clear();
        foreach (var row in Tabs.SelectMany(t => t.LoadTrashRows()))
        {
            TrashRows.Add(row);
        }
        OnPropertyChanged(nameof(TrashCount));
        OnPropertyChanged(nameof(TrashButtonLabel));
    }

    private static int ComputeDefaultSelectedTabIndex(IReadOnlyList<AccountTabViewModel> tabs)
    {
        if (tabs.Count == 3 && tabs[2].ProviderKey == "gemini-pro")
        {
            return 2;
        }

        return 0;
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
