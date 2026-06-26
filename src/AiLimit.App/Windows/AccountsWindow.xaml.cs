using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using AiLimit.App.ViewModels.Accounts;

namespace AiLimit.App.Windows;

public partial class AccountsWindow : Window
{
    private readonly AccountsWindowViewModel _viewModel;

    public AccountsWindow(AccountsWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
        RefreshModeVisibility();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountsWindowViewModel.IsTrashMode))
        {
            RefreshModeVisibility();
        }
    }

    private void RefreshModeVisibility()
    {
        AccountsTabsView.Visibility = _viewModel.IsTrashMode ? Visibility.Collapsed : Visibility.Visible;
        TrashView.Visibility = _viewModel.IsTrashMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }
}
