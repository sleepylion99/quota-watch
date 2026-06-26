using System.Threading;
using System.Windows;
using AiLimit.App.ViewModels.Accounts;
using WpfButton = System.Windows.Controls.Button;

namespace AiLimit.App.Controls;

public partial class AccountTrashView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty PendingDeleteTitleTextProperty =
        DependencyProperty.Register(
            nameof(PendingDeleteTitleText),
            typeof(string),
            typeof(AccountTrashView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PendingDeleteMessageTextProperty =
        DependencyProperty.Register(
            nameof(PendingDeleteMessageText),
            typeof(string),
            typeof(AccountTrashView),
            new PropertyMetadata(string.Empty));

    private TrashAccountRowViewModel? _pendingDeleteRow;

    public AccountTrashView()
    {
        InitializeComponent();
    }

    public string PendingDeleteTitleText
    {
        get => (string)GetValue(PendingDeleteTitleTextProperty);
        set => SetValue(PendingDeleteTitleTextProperty, value);
    }

    public string PendingDeleteMessageText
    {
        get => (string)GetValue(PendingDeleteMessageTextProperty);
        set => SetValue(PendingDeleteMessageTextProperty, value);
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountsWindowViewModel vm)
        {
            await vm.CloseTrashAsync(CancellationToken.None);
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: TrashAccountRowViewModel row }
            && DataContext is AccountsWindowViewModel vm)
        {
            await vm.RestoreTrashAsync(row, CancellationToken.None);
        }
    }

    private void DeletePermanentlyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: TrashAccountRowViewModel row }
            || DataContext is not AccountsWindowViewModel vm)
        {
            return;
        }

        _pendingDeleteRow = row;
        PendingDeleteTitleText = vm.PermanentDeleteTitleText;
        PendingDeleteMessageText = vm.FormatPermanentDeleteMessage(row);
        PermanentDeleteOverlay.Visibility = Visibility.Visible;
        ConfirmPermanentDeleteButton.Focus();
    }

    private void CancelPermanentDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        ClearPendingDelete();
    }

    private async void ConfirmPermanentDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteRow is not { } row
            || DataContext is not AccountsWindowViewModel vm)
        {
            ClearPendingDelete();
            return;
        }

        await vm.DeleteTrashPermanentlyAsync(row, CancellationToken.None);
        ClearPendingDelete();
    }

    private void ClearPendingDelete()
    {
        _pendingDeleteRow = null;
        PendingDeleteTitleText = string.Empty;
        PendingDeleteMessageText = string.Empty;
        PermanentDeleteOverlay.Visibility = Visibility.Collapsed;
    }
}
