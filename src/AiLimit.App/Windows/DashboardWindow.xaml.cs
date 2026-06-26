using System.Windows;
using System.Windows.Media.Animation;
using AiLimit.App.Services;
using AiLimit.App.ViewModels;
using AiLimit.App.ViewModels.Accounts;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;

namespace AiLimit.App.Windows;

public partial class DashboardWindow : Window
{
    private readonly AppState _state;
    private readonly UsageViewModel _viewModel = new();
    private SettingsWindow? _settingsWindow;
    private AccountsWindow? _accountsWindow;
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
        if (e.ClickCount >= 2)
        {
            WindowState = WindowState.Normal;
            return;
        }

        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            RestoreMaximizedWindowForDrag(e);
        }

        DragMove();
    }

    private void RestoreMaximizedWindowForDrag(System.Windows.Input.MouseButtonEventArgs e)
    {
        var pointer = e.GetPosition(this);
        var horizontalRatio = ActualWidth <= 0 ? 0.5 : Math.Clamp(pointer.X / ActualWidth, 0, 1);
        var restoreWidth = Math.Max(RestoreBounds.Width, MinWidth);
        var restoreHeight = Math.Max(RestoreBounds.Height, MinHeight);
        var screenPointer = PointToScreen(pointer);
        var workArea = SystemParameters.WorkArea;

        WindowState = WindowState.Normal;
        Width = restoreWidth;
        Height = restoreHeight;
        Left = Math.Clamp(
            screenPointer.X - restoreWidth * horizontalRatio,
            workArea.Left,
            workArea.Right - restoreWidth);
        Top = Math.Clamp(
            screenPointer.Y - Math.Min(pointer.Y, 30),
            workArea.Top,
            workArea.Bottom - restoreHeight);
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

    private void AccountsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_accountsWindow is { IsVisible: true })
        {
            _accountsWindow.Activate();
            return;
        }

        var providers = new IAccountProvider[]
        {
            AppState.GetOrCreateCodexAccountProvider(),
            AppState.GetOrCreateClaudeAccountProvider(),
            AppState.GetOrCreateAntigravityAccountProvider()
        };

        var registry = AppState.GetOrCreateAntigravityClientRegistry();
        var vm = new AccountsWindowViewModel(
            providers,
            _state.DisplayLanguage,
            antigravitySignIn: AppState.BuildAntigravitySignIn(),
            antigravityOAuthClientPanel: new AntigravityOAuthClientPanelViewModel(registry),
            claudeSignIn: AppState.BuildClaudeSignIn());
        vm.ActiveAccountChanged += async (_, _) =>
        {
            try
            {
                await _state.RefreshNowAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Write(
                    AppLogLevel.Warning,
                    "Dashboard",
                    $"Accounts window refresh after switch failed. {ex.GetType().Name}: {ex.Message}");
            }
        };

        _accountsWindow = new AccountsWindow(vm)
        {
            Owner = this
        };
        _accountsWindow.Closed += (_, _) => _accountsWindow = null;

        // Tabs auto-load when they become visible (see AccountsTable); no explicit reload needed.
        _accountsWindow.Show();
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
        NativeTopmost.Apply(this, isAlwaysOnTop);
        DashboardPinButton.IsChecked = isAlwaysOnTop;
    }

    private void DashboardPinButton_Click(object sender, RoutedEventArgs e)
    {
        var isAlwaysOnTop = DashboardPinButton.IsChecked == true;
        NativeTopmost.Apply(this, isAlwaysOnTop);
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

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyAlwaysOnTop();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _state.ThemeService?.ReevaluateSystemTheme();
        ApplyAlwaysOnTop();
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
            widgetOpacity: _state.CurrentSettings.WidgetOpacity,
            predictions: _state.CurrentPredictions);
        AppLog.Write(
            "Dashboard",
            $"ViewModel updated. snapshots={_state.CurrentSnapshots.Count}, settingsRows={_viewModel.ProviderSettings.Count}, refreshing={_state.IsRefreshing}, displayMode={LimitDisplayMode.Bars}, language={_state.CurrentSettings.Language}");
    }
}
