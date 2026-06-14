using System.Windows;
using System.Windows.Input;
using AiLimit.App.Services;
using AiLimit.App.ViewModels;
using AiLimit.Core.Settings;

namespace AiLimit.App.Windows;

public partial class WidgetWindow : Window
{
    private readonly AppState _state;
    private readonly UsageViewModel _viewModel = new();
    private bool _isApplyingWindowOpacity;

    public WidgetWindow(AppState state)
    {
        InitializeComponent();

        _state = state;
        DataContext = _viewModel;
        UpdateViewModel();
        ApplyWindowOpacity();
        ApplyAlwaysOnTop();

        _state.SnapshotChanged += OnSnapshotChanged;
        _state.SettingsChanged += OnSettingsChanged;
        SourceInitialized += OnSourceInitialized;
        Activated += OnActivated;
        if (_state.ThemeService is not null)
        {
            _state.ThemeService.ThemeChanged += OnThemeChanged;
        }
        Closed += (_, _) =>
        {
            _state.SnapshotChanged -= OnSnapshotChanged;
            _state.SettingsChanged -= OnSettingsChanged;
            SourceInitialized -= OnSourceInitialized;
            Activated -= OnActivated;
            if (_state.ThemeService is not null)
            {
                _state.ThemeService.ThemeChanged -= OnThemeChanged;
            }
        };
        LocationChanged += (_, _) => _state.UpdateWidgetPlacement(Left, Top);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _state.RefreshNowAsync();
    }

    private void OpenDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        _state.ShowDashboard();
    }

    private void ShowMoreHint_Click(object sender, RoutedEventArgs e)
    {
        _state.ShowDashboard();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        _state.ToggleWidget();
    }

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnSnapshotChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateViewModel);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateViewModel();
            ApplyWindowOpacity();
            ApplyAlwaysOnTop();
        });
    }

    private void ApplyAlwaysOnTop()
    {
        var isAlwaysOnTop = _state.CurrentSettings.IsWidgetAlwaysOnTop;
        NativeTopmost.Apply(this, isAlwaysOnTop);
        WidgetPinButton.IsChecked = isAlwaysOnTop;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyAlwaysOnTop();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        ApplyAlwaysOnTop();
    }

    private void WidgetPinButton_Click(object sender, RoutedEventArgs e)
    {
        var isAlwaysOnTop = WidgetPinButton.IsChecked == true;
        NativeTopmost.Apply(this, isAlwaysOnTop);
        _state.SetWidgetAlwaysOnTop(isAlwaysOnTop);
    }

    private void ApplyWindowOpacity()
    {
        var opacity = AppSettings.ClampOpacity(_state.CurrentSettings.WidgetOpacity);
        _isApplyingWindowOpacity = true;
        try
        {
            WidgetOpacitySlider.SetCurrentValue(
                System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                opacity * 100);
            Opacity = opacity;
            WidgetOpacityValueText.Text = $"{(int)Math.Round(opacity * 100)}%";
        }
        finally
        {
            _isApplyingWindowOpacity = false;
        }
    }

    private void WidgetOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isApplyingWindowOpacity)
        {
            return;
        }

        var opacity = e.NewValue / 100.0;
        _state.SetWidgetOpacity(opacity);
        Opacity = AppSettings.ClampOpacity(opacity);
        WidgetOpacityValueText.Text = $"{(int)Math.Round(e.NewValue)}%";
    }

    private void OnThemeChanged(object? sender, ResolvedTheme theme)
    {
        Dispatcher.Invoke(_viewModel.RaiseColorProperties);
    }

    private void UpdateViewModel()
    {
        _viewModel.Update(
            _state.CurrentSnapshots,
            _state.IsRefreshing,
            _state.CurrentSettings.LimitDisplayMode,
            AppLanguageResolver.Resolve(_state.DisplayLanguage),
            _state.CurrentSettings.GetEffectiveProviders(),
            themeMode: _state.CurrentSettings.ThemeMode,
            dashboardOpacity: _state.CurrentSettings.DashboardOpacity,
            widgetOpacity: _state.CurrentSettings.WidgetOpacity,
            predictions: _state.CurrentPredictions);
    }
}
