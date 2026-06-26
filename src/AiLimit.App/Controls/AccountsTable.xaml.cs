using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AiLimit.App.ViewModels.Accounts;
using AiLimit.App.Windows;

namespace AiLimit.App.Controls;

public partial class AccountsTable : System.Windows.Controls.UserControl
{
    private AccountTabViewModel? _tab;

    public AccountsTable()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_tab is not null)
        {
            _tab.PropertyChanged -= OnTabPropertyChanged;
            _tab.Rows.CollectionChanged -= OnRowsCollectionChanged;
        }

        _tab = e.NewValue as AccountTabViewModel;

        if (_tab is not null)
        {
            _tab.PropertyChanged += OnTabPropertyChanged;
            _tab.Rows.CollectionChanged += OnRowsCollectionChanged;
        }

        RefreshVisualState();

        // Auto-load the tab the first time it becomes visible (on window open and on
        // tab switch) so the user doesn't have to press "Refresh All" to see the list.
        // Guarded on an empty list so switching back to an already-loaded tab doesn't
        // re-poll; "Refresh All" remains the way to force a refresh.
        if (_tab is { } tab && tab.Rows.Count == 0)
        {
            _ = AutoLoadAsync(tab);
        }
    }

    private static async Task AutoLoadAsync(AccountTabViewModel tab)
    {
        try
        {
            await tab.RefreshAndRescanAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Best-effort: any failure surfaces through the tab's own error banner / row status.
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_tab is not null)
        {
            _tab.PropertyChanged -= OnTabPropertyChanged;
            _tab.Rows.CollectionChanged -= OnRowsCollectionChanged;
            _tab = null;
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshVisualState();
    }

    private void OnRowsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        var tab = _tab;
        var isEmpty = tab is null || tab.Rows.Count == 0;
        EmptyStateText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RowContainer.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

        var error = tab?.SyncErrorMessage;
        if (string.IsNullOrWhiteSpace(error))
        {
            SyncErrorBanner.Visibility = Visibility.Collapsed;
            SyncErrorText.Text = string.Empty;
        }
        else
        {
            SyncErrorBanner.Visibility = Visibility.Visible;
            SyncErrorText.Text = error;
        }
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountTabViewModel vm)
        {
            if (vm.SupportsProfileCreation)
                await vm.CreateProfileAsync(CancellationToken.None);
            else
                await vm.SyncFromLocalAsync(CancellationToken.None);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountTabViewModel tab)
        {
            await tab.RefreshAndRescanAsync(CancellationToken.None);
        }
    }

    private async void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Guid id })
        {
            return;
        }

        var hostWindow = Window.GetWindow(this) as AccountsWindow;
        if (hostWindow?.DataContext is AccountsWindowViewModel windowVm)
        {
            await windowVm.SwitchInCurrentTabAsync(id, CancellationToken.None);
        }
    }

    private async void TrashButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Guid id })
        {
            return;
        }

        var hostWindow = Window.GetWindow(this) as AccountsWindow;
        if (hostWindow?.DataContext is AccountsWindowViewModel windowVm)
        {
            await windowVm.MoveToTrashInCurrentTabAsync(id, CancellationToken.None);
        }
    }

    private async void GoogleSignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountTabViewModel vm)
        {
            await vm.SignInWithGoogleAsync(CancellationToken.None);
        }
    }

    private void ClaudeSignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountTabViewModel vm)
        {
            vm.ClaudeSignIn?.Begin();
        }
    }

    private async void ClaudeSubmitCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountTabViewModel vm)
        {
            await vm.CompleteClaudeSignInAsync(CancellationToken.None);
        }
    }

    private async void OpenTrashButton_Click(object sender, RoutedEventArgs e)
    {
        var hostWindow = Window.GetWindow(this) as AccountsWindow;
        if (hostWindow?.DataContext is AccountsWindowViewModel windowVm)
        {
            await windowVm.OpenTrashAsync(CancellationToken.None);
        }
    }
}
