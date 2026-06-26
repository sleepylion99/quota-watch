using System.Windows;
using AiLimit.App.Localization;
using AiLimit.App.Services;
using AiLimit.App.ViewModels;
using AiLimit.App.ViewModels.Accounts;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;

namespace AiLimit.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly UsageViewModel _viewModel = new();
    private HashSet<string> _pendingEnabledProviderIds = new(StringComparer.Ordinal);
    private AppLanguage _pendingLanguage;
    private bool _pendingLimitWarningEnabled;
    private bool _pendingInactiveAccountWarning;
    private List<ProviderLimitWarningSetting> _pendingLimitWarningSettings = [];
    private HashSet<string> _originalEnabledProviderIds = new(StringComparer.Ordinal);
    private AppLanguage _originalLanguage;
    private bool _originalLimitWarningEnabled;
    private bool _originalInactiveAccountWarning;
    private List<ProviderLimitWarningSetting> _originalLimitWarningSettings = [];
    private AccountsWindow? _accountsWindow;
    private bool _hasPendingSettingsChanges;
    private bool HasPendingSettingsChanges
    {
        get => _hasPendingSettingsChanges;
        set
        {
            _hasPendingSettingsChanges = value;
            SettingsStatusEllipse.Fill = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource(
                value ? AiLimit.App.Theming.BrushKey.IndicatorDirty : AiLimit.App.Theming.BrushKey.IndicatorClean);
        }
    }
    private bool _isUpdatingSettingsView;
    private bool _hasStartedInitialUpdateCheck;
    private bool _isCheckingForUpdates;
    private readonly UpdateReleaseLauncher _updateReleaseLauncher = new UpdateReleaseLauncher();
    private string? _pendingUpdateReleaseUrl;

    public SettingsWindow(AppState state)
    {
        InitializeComponent();

        _state = state;
        DataContext = _viewModel;
        CapturePendingSettingsFromState();

        _isUpdatingSettingsView = true;
        try
        {
            UpdateViewModel();
        }
        finally
        {
            _isUpdatingSettingsView = false;
        }

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasStartedInitialUpdateCheck)
        {
            return;
        }

        _hasStartedInitialUpdateCheck = true;
        await CheckForUpdatesAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SettingsMinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ProviderSettingCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsView)
        {
            return;
        }

        if (sender is System.Windows.Controls.CheckBox { Tag: string providerId, IsChecked: bool isChecked })
        {
            AppLog.Write("Settings", $"Provider checkbox pending. provider={providerId}, isChecked={isChecked}");
            if (isChecked)
            {
                _pendingEnabledProviderIds.Add(providerId);
            }
            else
            {
                _pendingEnabledProviderIds.Remove(providerId);
            }

            RecomputePendingSettingsChanges();
            UpdatePendingSettingsPreview();
        }
    }

    private void OpenAccountsWindowButton_Click(object sender, RoutedEventArgs e)
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
                    "Settings",
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

    private void LanguageOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsView)
        {
            return;
        }

        if (sender is System.Windows.Controls.Button { Tag: AppLanguage language })
        {
            _pendingLanguage = language;
            _state.PreviewLanguage(language);
            RecomputePendingSettingsChanges();
            UpdatePendingSettingsPreview();
        }
    }

    private void LimitWarningEnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsView)
        {
            return;
        }

        if (sender is System.Windows.Controls.CheckBox { IsChecked: bool isChecked })
        {
            _pendingLimitWarningEnabled = isChecked;
            RecomputePendingSettingsChanges();
            UpdatePendingSettingsPreview();
        }
    }

    private void InactiveAccountWarningCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsView)
        {
            return;
        }

        if (sender is System.Windows.Controls.CheckBox { IsChecked: bool isChecked })
        {
            _pendingInactiveAccountWarning = isChecked;
            RecomputePendingSettingsChanges();
            UpdatePendingSettingsPreview();
        }
    }

    private void LimitWarningRecommendationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsView)
        {
            return;
        }

        if (sender is System.Windows.Controls.Button
            {
                DataContext: LimitWarningRecommendationViewModel recommendation
            })
        {
            UpdatePendingLimitWarningSetting(
                recommendation.ProviderId,
                recommendation.Percent,
                isCustom: false);
            UpdatePendingSettingsPreview();
        }
    }

    private void LimitWarningCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSettingsView)
        {
            return;
        }

        if (sender is System.Windows.Controls.Button
            {
                DataContext: ProviderLimitWarningSettingItemViewModel provider
            })
        {
            UpdatePendingLimitWarningSetting(
                provider.ProviderId,
                provider.ThresholdPercent,
                isCustom: true);
            UpdatePendingSettingsPreview();
        }
    }

    private void LimitWarningSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSettingsView)
        {
            return;
        }

        if (sender is System.Windows.Controls.Slider
            {
                DataContext: ProviderLimitWarningSettingItemViewModel
                {
                    IsCustom: true
                } provider
            })
        {
            UpdatePendingLimitWarningSetting(
                provider.ProviderId,
                (int)Math.Round(e.NewValue, MidpointRounding.AwayFromZero),
                isCustom: true);
            provider.SetCustomThreshold(
                (int)Math.Round(e.NewValue, MidpointRounding.AwayFromZero));
        }
    }

    private void UpdatePendingLimitWarningSetting(string providerId, int percent, bool isCustom)
    {
        var updated = new ProviderLimitWarningSetting(
            providerId,
            Math.Clamp(percent, 1, 99),
            isCustom);
        var index = _pendingLimitWarningSettings.FindIndex(setting =>
            setting.ProviderId.Equals(providerId, StringComparison.Ordinal));
        if (index >= 0)
        {
            _pendingLimitWarningSettings[index] = updated;
        }
        else
        {
            _pendingLimitWarningSettings.Add(updated);
        }

        RecomputePendingSettingsChanges();
    }

    private void SettingsCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hasPendingSettingsChanges)
        {
            UnsavedChangesPanel.Visibility = Visibility.Visible;
            return;
        }

        Close();
    }

    private void SettingsApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPendingSettings();
        _state.ClearLanguagePreview();
        _originalEnabledProviderIds = new HashSet<string>(_pendingEnabledProviderIds, StringComparer.Ordinal);
        _originalLanguage = _pendingLanguage;
        _originalLimitWarningEnabled = _pendingLimitWarningEnabled;
        _originalInactiveAccountWarning = _pendingInactiveAccountWarning;
        _originalLimitWarningSettings = _pendingLimitWarningSettings.Select(setting => setting).ToList();
        HasPendingSettingsChanges = false;
        Close();
    }

    private void KeepEditingSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        UnsavedChangesPanel.Visibility = Visibility.Collapsed;
    }

    private void DiscardSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        UnsavedChangesPanel.Visibility = Visibility.Collapsed;
        _state.ClearLanguagePreview();
        HasPendingSettingsChanges = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _state.ClearLanguagePreview();
        base.OnClosed(e);
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private void CopyDiagnosticLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(AppLog.ReadDiagnosticLogForCopy());
            AppLog.Write("Settings", "Diagnostic log copied from settings.");
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, "Settings", $"Diagnostic log copy failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_isCheckingForUpdates || UpdateAvailableOverlay.Visibility != Visibility.Collapsed)
        {
            return;
        }

        _isCheckingForUpdates = true;
        var language = _state.DisplayLanguage;
        _viewModel.SetUpdateCheckStatus(AppText.Get(language, AppStringKeys.UpdateCheckStatusChecking));
        try
        {
            var result = await _state.CheckForUpdatesAsync();
            var status = result.IsUpdateAvailable
                ? AppText.Get(language, AppStringKeys.UpdateCheckStatusAvailable, result.LatestVersion, result.ReleaseUrl ?? string.Empty)
                : AppText.Get(language, AppStringKeys.UpdateCheckStatusUpToDate, result.CurrentVersion);
            _viewModel.SetUpdateCheckStatus(status);

            if (result.IsUpdateAvailable)
            {
                _pendingUpdateReleaseUrl = result.ReleaseUrl;
                _viewModel.SetUpdateAvailablePrompt(result.LatestVersion);
                UpdateAvailableOverlay.Visibility = Visibility.Visible;
                ConfirmUpdateButton.Focus();
            }
        }
        catch (UpdateCheckException ex)
        {
            AppLog.Write(AppLogLevel.Warning, "Settings", $"Update check failed. {ex.GetType().Name}: {ex.Message}");
            _viewModel.SetUpdateCheckStatus(AppText.Get(language, AppStringKeys.UpdateCheckReleaseNotAccessible));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write(AppLogLevel.Warning, "Settings", $"Update check failed. {ex.GetType().Name}: {ex.Message}");
            _viewModel.SetUpdateCheckStatus(AppText.Get(language, AppStringKeys.UpdateCheckStatusFailed));
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private void ConfirmUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateAvailableOverlay.Visibility = Visibility.Collapsed;

        try
        {
            _updateReleaseLauncher.Open(_pendingUpdateReleaseUrl);
        }
        catch (Exception ex)
        {
            AppLog.Write(
                AppLogLevel.Warning,
                "Settings",
                $"Update release page open failed. {ex.GetType().Name}: {ex.Message}");
            _viewModel.SetUpdateCheckStatus(_viewModel.UpdateReleaseOpenFailedText);
        }
        finally
        {
            _pendingUpdateReleaseUrl = null;
        }
    }

    private void CancelUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateAvailableOverlay.Visibility = Visibility.Collapsed;
        _pendingUpdateReleaseUrl = null;
    }

    private void CapturePendingSettingsFromState()
    {
        _pendingEnabledProviderIds = _state.CurrentSettings.GetEffectiveProviders()
            .Where(provider => provider.IsEnabled)
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);
        _pendingLanguage = _state.CurrentSettings.Language;
        _pendingLimitWarningEnabled = _state.CurrentSettings.IsLimitWarningEnabled;
        _pendingInactiveAccountWarning = _state.CurrentSettings.IsInactiveAccountWarningEnabled;
        _pendingLimitWarningSettings = (_state.CurrentSettings.Normalize().LimitWarningSettings ?? []).ToList();

        _originalEnabledProviderIds = new HashSet<string>(_pendingEnabledProviderIds, StringComparer.Ordinal);
        _originalLanguage = _pendingLanguage;
        _originalLimitWarningEnabled = _pendingLimitWarningEnabled;
        _originalInactiveAccountWarning = _pendingInactiveAccountWarning;
        _originalLimitWarningSettings = _pendingLimitWarningSettings.Select(setting => setting).ToList();

        HasPendingSettingsChanges = false;
    }

    private void RecomputePendingSettingsChanges()
    {
        HasPendingSettingsChanges = !_pendingEnabledProviderIds.SetEquals(_originalEnabledProviderIds)
            || _pendingLanguage != _originalLanguage
            || _pendingLimitWarningEnabled != _originalLimitWarningEnabled
            || _pendingInactiveAccountWarning != _originalInactiveAccountWarning
            || !LimitWarningSettingsEqual(_pendingLimitWarningSettings, _originalLimitWarningSettings);
    }

    private static bool LimitWarningSettingsEqual(
        IReadOnlyList<ProviderLimitWarningSetting> a,
        IReadOnlyList<ProviderLimitWarningSetting> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var lookup = b.ToDictionary(setting => setting.ProviderId, StringComparer.Ordinal);
        foreach (var setting in a)
        {
            if (!lookup.TryGetValue(setting.ProviderId, out var other) || other != setting)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdatePendingSettingsPreview()
    {
        _isUpdatingSettingsView = true;
        try
        {
            _viewModel.Update(
                _state.CurrentSnapshots,
                _state.IsRefreshing,
                LimitDisplayMode.Bars,
                AppLanguageResolver.Resolve(_pendingLanguage),
                PendingProviderSettings(),
                _state.AutoRefreshStatuses,
                _pendingLimitWarningEnabled,
                _state.CurrentSettings.LimitWarningThresholdPercent,
                AntigravityUsageProvider.GetActiveOAuthClientOrigin(),
                _pendingLimitWarningSettings,
                _state.CurrentSettings.ThemeMode,
                _state.CurrentSettings.DashboardOpacity,
                _state.CurrentSettings.WidgetOpacity,
                isInactiveAccountWarningEnabled: _pendingInactiveAccountWarning);

        }
        finally
        {
            _isUpdatingSettingsView = false;
        }
    }

    private IReadOnlyList<ProviderSetting> PendingProviderSettings()
    {
        return _state.CurrentSettings.GetEffectiveProviders()
            .Select(provider => provider with
            {
                IsEnabled = _pendingEnabledProviderIds.Contains(provider.Id),
                Mode = ProviderSetting.NormalizeMode(provider.Id, provider.Mode)
            })
            .ToList();
    }

    private void ApplyPendingSettings()
    {
        var updatedSettings = _state.CurrentSettings with
        {
            Language = _pendingLanguage,
            IsLimitWarningEnabled = _pendingLimitWarningEnabled,
            IsInactiveAccountWarningEnabled = _pendingInactiveAccountWarning,
            LimitWarningThresholdPercent = _pendingLimitWarningSettings
                .First(setting => setting.ProviderId == "codex")
                .ThresholdPercent,
            LimitWarningSettings = _pendingLimitWarningSettings.ToList(),
            Providers = PendingProviderSettings()
        };
        _state.SaveSettingsFromDashboard(updatedSettings);
    }

    private void UpdateViewModel()
    {
        _viewModel.Update(
            _state.CurrentSnapshots,
            _state.IsRefreshing,
            LimitDisplayMode.Bars,
            AppLanguageResolver.Resolve(_state.CurrentSettings.Language),
            _state.CurrentSettings.GetEffectiveProviders(),
            _state.AutoRefreshStatuses,
            _state.CurrentSettings.IsLimitWarningEnabled,
            _state.CurrentSettings.LimitWarningThresholdPercent,
            AntigravityUsageProvider.GetActiveOAuthClientOrigin(),
            _pendingLimitWarningSettings,
            _state.CurrentSettings.ThemeMode,
            _state.CurrentSettings.DashboardOpacity,
            _state.CurrentSettings.WidgetOpacity,
            isInactiveAccountWarningEnabled: _state.CurrentSettings.IsInactiveAccountWarningEnabled);
    }
}
