using System.Windows;
using System.Windows.Media.Animation;
using AiLimit.App.Services;
using AiLimit.App.ViewModels;
using AiLimit.Core.Providers;
using AiLimit.Core.Settings;

namespace AiLimit.App.Windows;

public partial class DashboardWindow : Window
{
    private readonly AppState _state;
    private readonly UsageViewModel _viewModel = new();
    private SettingsWindow? _settingsWindow;
    private bool _isApplyingWindowOpacity;

    public DashboardWindow(AppState state)
    {
        InitializeComponent();

        _state = state;
        DataContext = _viewModel;
        UpdateViewModel();
        UpdateThemeToggle(_state.CurrentSettings.ThemeMode, animate: false);
        ApplyWindowOpacity();
        ApplyAlwaysOnTop();

        _state.SnapshotChanged += OnSnapshotChanged;
        _state.SettingsChanged += OnSettingsChanged;
        Activated += OnActivated;
        if (_state.ThemeService is not null)
        {
            _state.ThemeService.ThemeChanged += OnThemeChanged;
        }
        Closed += (_, _) =>
        {
            _state.SnapshotChanged -= OnSnapshotChanged;
            _state.SettingsChanged -= OnSettingsChanged;
            Activated -= OnActivated;
            if (_state.ThemeService is not null)
            {
                _state.ThemeService.ThemeChanged -= OnThemeChanged;
            }
        };
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _state.RefreshNowAsync();
    }

    private void ToggleWidgetButton_Click(object sender, RoutedEventArgs e)
    {
        _state.ToggleWidget();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ToggleExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ProviderUsageItemViewModel vm })
        {
            vm.ToggleExpand();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_state)
        {
            Owner = this,
            Left = Left + Width - 560,
            Top = Top + 60
        };
        _settingsWindow.Show();
    }

    private void ThemeModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value }
            && Enum.TryParse<AppThemeMode>(value, ignoreCase: true, out var mode))
        {
            _state.SetThemeMode(mode);
        }
    }

    internal static bool TryOpenAntigravityOAuthGuide(
        AntigravityOAuthGuide guide,
        AppLanguage language,
        UsageViewModel viewModel)
    {
        try
        {
            guide.Open(AppLanguageResolver.Resolve(language));
            AppLog.Write("Dashboard", $"Antigravity OAuth setup guide opened. language={language}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(
                AppLogLevel.Warning,
                "Dashboard",
                $"Antigravity OAuth setup guide open failed. {ex.GetType().Name}: {ex.Message}");
            viewModel.SetAntigravityOAuthStatus(
                UsageViewModel.AntigravityOAuthGuideOpenFailedMessage(language));
            return false;
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
            UpdateThemeToggle(_state.CurrentSettings.ThemeMode, animate: true);
            ApplyWindowOpacity();
            ApplyAlwaysOnTop();
        });
    }

    private void ApplyAlwaysOnTop()
    {
        var isAlwaysOnTop = _state.CurrentSettings.IsDashboardAlwaysOnTop;
        Topmost = isAlwaysOnTop;
        DashboardPinButton.IsChecked = isAlwaysOnTop;
    }

    private void DashboardPinButton_Click(object sender, RoutedEventArgs e)
    {
        var isAlwaysOnTop = DashboardPinButton.IsChecked == true;
        Topmost = isAlwaysOnTop;
        _state.SetDashboardAlwaysOnTop(isAlwaysOnTop);
    }

    private void ApplyWindowOpacity()
    {
        var opacity = AppSettings.ClampOpacity(_state.CurrentSettings.DashboardOpacity);
        _isApplyingWindowOpacity = true;
        try
        {
            DashboardOpacitySlider.SetCurrentValue(
                System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                opacity * 100);
            Opacity = opacity;
            DashboardOpacityValueText.Text = $"{(int)Math.Round(opacity * 100)}%";
        }
        finally
        {
            _isApplyingWindowOpacity = false;
        }
    }

    private void DashboardOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isApplyingWindowOpacity)
        {
            return;
        }

        var opacity = e.NewValue / 100.0;
        _state.SetDashboardOpacity(opacity);
        Opacity = AppSettings.ClampOpacity(opacity);
        DashboardOpacityValueText.Text = $"{(int)Math.Round(e.NewValue)}%";
    }

    private void OnThemeChanged(object? sender, ResolvedTheme theme)
    {
        Dispatcher.Invoke(_viewModel.RaiseColorProperties);
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _state.ThemeService?.ReevaluateSystemTheme();
    }

    private void UpdateThemeToggle(AppThemeMode mode, bool animate)
    {
        var target = mode switch
        {
            AppThemeMode.System => 43d,
            AppThemeMode.Light => 86d,
            _ => 0d
        };

        if (!animate)
        {
            ThemeToggleThumbTransform.X = target;
            return;
        }

        ThemeToggleThumbTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation
            {
                From = ThemeToggleThumbTransform.X,
                To = target,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void UpdateViewModel()
    {
        _viewModel.Update(
            _state.CurrentSnapshots,
            _state.IsRefreshing,
            LimitDisplayMode.Bars,
            AppLanguageResolver.Resolve(_state.DisplayLanguage),
            _state.CurrentSettings.GetEffectiveProviders(),
            _state.AutoRefreshStatuses,
            _state.CurrentSettings.IsLimitWarningEnabled,
            _state.CurrentSettings.LimitWarningThresholdPercent,
            AntigravityUsageProvider.GetActiveOAuthClientOrigin(),
            themeMode: _state.CurrentSettings.ThemeMode,
            dashboardOpacity: _state.CurrentSettings.DashboardOpacity,
            widgetOpacity: _state.CurrentSettings.WidgetOpacity);
        AppLog.Write(
            "Dashboard",
            $"ViewModel updated. snapshots={_state.CurrentSnapshots.Count}, settingsRows={_viewModel.ProviderSettings.Count}, refreshing={_state.IsRefreshing}, displayMode={LimitDisplayMode.Bars}, language={_state.CurrentSettings.Language}");
    }
}
