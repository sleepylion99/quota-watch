using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AiLimit.App.Localization;
using AiLimit.App.Services;
using AiLimit.App.Theming;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Settings;

namespace AiLimit.App.ViewModels;

public sealed class UsageViewModel : INotifyPropertyChanged
{
    private AppLanguage _displayLanguage = AppLanguage.English;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string HeaderTitle { get; private set; } = "Quota Watch";
    public string HeaderSubtitle { get; private set; } = "Waiting for provider snapshots";
    public string AppTagline { get; private set; } = "Windows control room";
    public string StatusText { get; private set; } = "Not refreshed";
    public string StatusBrush { get; private set; } = BrushKey.StatusNeutral;
    public string CheckedAtText { get; private set; } = "Never";
    public string GuidanceTitle { get; private set; } = "Refresh to load usage";
    public string GuidanceDetail { get; private set; } = "No usage data is loaded yet.";
    public string GuidanceAction { get; private set; } = "Use Refresh All to check your current limits.";
    public string WidgetGuidanceText { get; private set; } = "Refresh to load usage";
    public string TrackedLabel { get; private set; } = "Tracked";
    public string OverallStatusLabel { get; private set; } = "Overall status";
    public string LastCheckedLabel { get; private set; } = "Last checked";
    public string FiveHourModeText { get; private set; } = "5h limit";
    public string WeeklyModeText { get; private set; } = "Weekly limit";
    public string BothModeText { get; private set; } = "5h + weekly";
    public string BarsModeText { get; private set; } = "Graph view";
    public string ToggleWidgetButtonText { get; private set; } = "Toggle Widget";
    public string DashboardButtonText { get; private set; } = "Dashboard";
    public string SettingsButtonText { get; private set; } = "Settings";
    public string SettingsPanelDetailText { get; private set; } = "Adjust dashboard behavior and support actions in one place.";
    public string SettingsApplyButtonText { get; private set; } = "Apply";
    public string SettingsCloseButtonText { get; private set; } = "Close";
    public string SettingsImmediateApplyText { get; private set; } = "Apply saves your pending changes.";
    public string SettingsUnsavedTitleText { get; private set; } = "Unsaved changes";
    public string SettingsUnsavedDetailText { get; private set; } = "Close settings without saving changes?";
    public string SettingsDiscardButtonText { get; private set; } = "Discard";
    public string SettingsKeepEditingButtonText { get; private set; } = "Back";
    public string RefreshButtonText { get; private set; } = "Refresh";
    public string RefreshAllButtonText { get; private set; } = "Refresh All";
    public string CopyDiagnosticLogButtonText { get; private set; } = "Copy Log";
    public string HideButtonText { get; private set; } = "Hide";
    public string PinWindowTooltipText { get; private set; } = "Keep window on top";
    public string ModelSettingsButtonText { get; private set; } = "Model Settings";
    public string ModelSettingsDetailText { get; private set; } = "Choose which providers appear on the dashboard.";
    public string AccountsButtonText { get; private set; } = "Accounts";
    public string AntigravityOAuthTitleText { get; private set; } = "Antigravity OAuth";
    public string AntigravityOAuthDetailText { get; private set; } = "Optional setup for checking Antigravity after the IDE is closed. Values are securely encrypted on this device.";
    public string AntigravityOAuthClientIdLabelText { get; private set; } = "OAuth client ID";
    public string AntigravityOAuthClientSecretLabelText { get; private set; } = "OAuth client secret";
    public string AntigravityOAuthSaveButtonText { get; private set; } = "Save";
    public string AntigravityOAuthClearButtonText { get; private set; } = "Clear";
    public string AntigravityOAuthGuideButtonText { get; private set; } = "View setup guide";
    public string AntigravityOAuthStatusText { get; private set; } = "No saved client values.";
    public string AntigravityOAuthActiveClientText { get; private set; } = string.Empty;
    public string SettingsAntigravityMovedToAccountsText { get; private set; } = "Antigravity sign-in and OAuth client are now managed in the Account Manager.";
    public string LanguageSettingsTitleText { get; private set; } = "Language";
    public string LanguageSettingsDetailText { get; private set; } = "Follow the system language by default, or choose a specific dashboard language.";
    public string ThemeSettingsTitleText { get; private set; } = "Theme";
    public string ThemeSettingsDetailText { get; private set; } = "Choose how the dashboard looks.";
    public string ThemeSystemHintText { get; private set; } = "Auto follows your Windows app theme.";
    public double DashboardOpacityPercent { get; private set; } = 100;
    public double WidgetOpacityPercent { get; private set; } = 100;
    public double WindowOpacityMinimumPercent => AppSettings.MinimumWindowOpacity * 100;
    public double WindowOpacityMaximumPercent => AppSettings.MaximumWindowOpacity * 100;
    public string LimitWarningSettingsTitleText { get; private set; } = "Limit alerts";
    public string LimitWarningSettingsDetailText { get; private set; } = "Choose when AI limit alerts should appear.";
    public string LimitWarningEnabledText { get; private set; } = "Enable limit alerts";
    public string LimitWarningThresholdText { get; private set; } = "Alert threshold";
    public string CustomThresholdLabelText { get; private set; } = "Custom:";
    public string InactiveAccountWarningText { get; private set; } = "Warn for inactive accounts too";
    public string InactiveAccountWarningHintText { get; private set; } = "Polls other profiles in the background (every 30 min) so you get depletion warnings without switching accounts.";
    public bool IsInactiveAccountWarningEnabled { get; private set; } = false;
    public string UpdateCheckTitleText { get; private set; } = "Updates";
    public string UpdateCheckDetailText { get; private set; } = "Check whether a newer release is available.";
    public string CheckForUpdatesButtonText { get; private set; } = "Check for Updates";
    public string UpdateCheckStatusText { get; private set; } = "Not checked yet.";
    public string UpdateAvailableTitleText { get; private set; } = "Update available";
    public string UpdateAvailableMessageText { get; private set; } = string.Empty;
    public string UpdateAvailableConfirmButtonText { get; private set; } = "Open update page";
    public string UpdateAvailableCancelButtonText { get; private set; } = "Cancel";
    public string UpdateReleaseOpenFailedText { get; private set; } = "Could not open the update page.";
    public string DiagnosticLogTitleText { get; private set; } = "Diagnostic log";
    public string DiagnosticLogDetailText { get; private set; } = "Found a bug? Copy this log and send it to the developer.";
    public string TrackProviderLabel { get; private set; } = "Track";

    public ObservableCollection<ProviderUsageItemViewModel> Providers { get; } = [];
    public ObservableCollection<ProviderSettingItemViewModel> ProviderSettings { get; } = [];
    public ObservableCollection<LanguageOptionViewModel> LanguageOptions { get; } = [];
    public ObservableCollection<ThemeOptionViewModel> ThemeOptions { get; } = [];
    public ObservableCollection<LimitWarningThresholdOptionViewModel> LimitWarningThresholdOptions { get; } = [];
    public ObservableCollection<ProviderLimitWarningSettingItemViewModel> LimitWarningProviderSettings { get; } = [];
    public bool IsLimitWarningEnabled { get; private set; } = true;

    public void Update(
        IReadOnlyList<UsageSnapshot> snapshots,
        bool isRefreshing,
        LimitDisplayMode displayMode,
        AppLanguage language,
        IReadOnlyList<ProviderSetting>? providerSettings = null,
        IReadOnlyDictionary<string, ProviderAutoRefreshStatus>? autoRefreshStatuses = null,
        bool isLimitWarningEnabled = true,
        int limitWarningThresholdPercent = 10,
        AntigravityOAuthClientOrigin? antigravityActiveClientOrigin = null,
        IReadOnlyList<ProviderLimitWarningSetting>? limitWarningSettings = null,
        AppThemeMode themeMode = AppThemeMode.System,
        double dashboardOpacity = 1.0,
        double widgetOpacity = 1.0,
        IReadOnlyDictionary<UsageWindowKey, DepletionPrediction>? predictions = null,
        bool isInactiveAccountWarningEnabled = false)
    {
        var displayLanguage = AppLanguageResolver.Resolve(language);
        _displayLanguage = displayLanguage;
        ApplyLanguage(displayLanguage);
        UpdateLanguageOptions(language, displayLanguage);
        UpdateThemeOptions(themeMode, displayLanguage);
        UpdateOpacityState(dashboardOpacity, widgetOpacity);
        UpdateLimitWarningSettings(
            isLimitWarningEnabled,
            limitWarningThresholdPercent,
            limitWarningSettings,
            displayLanguage,
            isInactiveAccountWarningEnabled);
        UpdateProviderSettings(providerSettings ?? [], displayLanguage);
        UpdateAntigravityOAuthActiveClient(antigravityActiveClientOrigin, displayLanguage);

        Providers.Clear();
        var settingsById = (providerSettings ?? []).ToDictionary(setting => setting.Id, StringComparer.Ordinal);
        var sortedSnapshots = snapshots.OrderByDescending(snapshot =>
        {
            var w = SelectPrimaryWindow(snapshot, displayMode);
            if (w is null) return -1;
            return IsUsedPercentSnapshot(snapshot) ? w.PercentRemaining : 100 - w.PercentRemaining;
        });
        foreach (var snapshot in sortedSnapshots)
        {
            settingsById.TryGetValue(snapshot.ProviderId, out var setting);
            ProviderAutoRefreshStatus? autoRefreshStatus = null;
            autoRefreshStatuses?.TryGetValue(snapshot.ProviderId, out autoRefreshStatus);
            Providers.Add(new ProviderUsageItemViewModel(
                snapshot,
                displayMode,
                displayLanguage,
                setting,
                autoRefreshStatus,
                predictions));
        }

        if (snapshots.Count == 0)
        {
            HeaderTitle = "Quota Watch";
            HeaderSubtitle = AppText.Get(displayLanguage, AppStringKeys.HeaderSubtitleNoData);
            StatusText = isRefreshing
                ? AppText.Get(displayLanguage, AppStringKeys.StatusRefreshing)
                : AppText.Get(displayLanguage, AppStringKeys.StatusNotRefreshed);
            StatusBrush = isRefreshing ? BrushKey.StatusRefreshing : BrushKey.StatusNeutral;
            CheckedAtText = AppText.Get(displayLanguage, AppStringKeys.CheckedAtNever);
            ApplyGuidance(snapshots, isRefreshing, displayLanguage);
            RaiseAll();
            return;
        }

        var latestCheckedAt = snapshots.Max(snapshot => snapshot.CheckedAt);
        var failedCount = snapshots.Count(snapshot => snapshot.Status == UsageStatus.Failed);

        HeaderTitle = FormatModelCount(snapshots.Count, displayLanguage);
        HeaderSubtitle = string.Join(" / ", snapshots.Select(snapshot => snapshot.DisplayName));
        StatusText = isRefreshing
            ? AppText.Get(displayLanguage, AppStringKeys.StatusRefreshing)
            : failedCount > 0
                ? AppText.Get(displayLanguage, AppStringKeys.StatusFailed, failedCount)
                : AppText.Get(displayLanguage, AppStringKeys.StatusAllFresh);
        StatusBrush = GetStatusBrush(failedCount > 0 ? UsageStatus.Failed : UsageStatus.Fresh, isRefreshing);
        CheckedAtText = FormatDate(latestCheckedAt, displayLanguage);

        ApplyGuidance(snapshots, isRefreshing, displayLanguage);
        RaiseAll();
    }

    private void UpdateProviderSettings(IReadOnlyList<ProviderSetting> settingsList, AppLanguage language)
    {
        var toRemove = ProviderSettings.Where(item => settingsList.All(setting => setting.Id != item.Id)).ToList();
        foreach (var item in toRemove)
        {
            ProviderSettings.Remove(item);
        }

        for (int i = 0; i < settingsList.Count; i++)
        {
            var setting = settingsList[i];
            var existing = ProviderSettings.FirstOrDefault(item => item.Id == setting.Id);
            if (existing is null)
            {
                ProviderSettings.Insert(i, new ProviderSettingItemViewModel(setting, language));
            }
            else
            {
                existing.IsEnabled = setting.IsEnabled;
                existing.SelectedMode = ProviderSetting.NormalizeMode(setting.Id, setting.Mode) ?? "auto";
                existing.ModeOptions = [];
                existing.SetupHintText = ProviderSettingItemViewModel.CreateSetupHint(setting.Id, language);
            }
        }
    }

    private void UpdateLanguageOptions(AppLanguage selectedLanguage, AppLanguage displayLanguage)
    {
        var supported = AppLanguageCatalog.SupportedLanguages;
        var toRemove = LanguageOptions.Where(option => supported.All(item => item.Language != option.Language)).ToList();
        foreach (var option in toRemove)
        {
            LanguageOptions.Remove(option);
        }

        for (int i = 0; i < supported.Count; i++)
        {
            var item = supported[i];
            var existing = LanguageOptions.FirstOrDefault(option => option.Language == item.Language);
            var label = AppLanguageCatalog.LabelFor(item, displayLanguage);
            var isSelected = item.Language == selectedLanguage;
            if (existing is null)
            {
                LanguageOptions.Insert(i, new LanguageOptionViewModel(item.Language, item.Code, label, isSelected));
            }
            else
            {
                existing.Code = item.Code;
                existing.Label = label;
                existing.IsSelected = isSelected;
            }
        }
    }

    private void UpdateThemeOptions(AppThemeMode selectedMode, AppLanguage displayLanguage)
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(new ThemeOptionViewModel(
            AppThemeMode.Dark,
            AppText.Get(displayLanguage, AppStringKeys.ThemeOptionDark),
            selectedMode == AppThemeMode.Dark));
        ThemeOptions.Add(new ThemeOptionViewModel(
            AppThemeMode.Light,
            AppText.Get(displayLanguage, AppStringKeys.ThemeOptionLight),
            selectedMode == AppThemeMode.Light));
        ThemeOptions.Add(new ThemeOptionViewModel(
            AppThemeMode.System,
            AppText.Get(displayLanguage, AppStringKeys.ThemeOptionSystem),
            selectedMode == AppThemeMode.System));
    }

    private void UpdateOpacityState(double dashboardOpacity, double widgetOpacity)
    {
        DashboardOpacityPercent = Math.Round(AppSettings.ClampOpacity(dashboardOpacity) * 100);
        WidgetOpacityPercent = Math.Round(AppSettings.ClampOpacity(widgetOpacity) * 100);
    }

    private void UpdateLimitWarningSettings(
        bool isEnabled,
        int selectedThresholdPercent,
        IReadOnlyList<ProviderLimitWarningSetting>? providerSettings,
        AppLanguage displayLanguage,
        bool isInactiveAccountWarningEnabled = false)
    {
        IsLimitWarningEnabled = isEnabled;
        IsInactiveAccountWarningEnabled = isInactiveAccountWarningEnabled;
        LimitWarningProviderSettings.Clear();
        var settingsById = (providerSettings ?? [])
            .GroupBy(setting => setting.ProviderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (var (providerId, providerName, isUsedPercent) in new[]
        {
            ("codex", "ChatGPT Codex", false),
            ("claude", "Claude Code", true),
            ("gemini-pro", "Google Antigravity", true)
        })
        {
            var fallbackPercent = isUsedPercent
                ? 100 - Math.Clamp(selectedThresholdPercent, 1, 99)
                : Math.Clamp(selectedThresholdPercent, 1, 99);
            var setting = settingsById.TryGetValue(providerId, out var saved)
                ? ProviderLimitWarningSettingItemViewModel.NormalizeSetting(saved, isUsedPercent)
                : ProviderLimitWarningSettingItemViewModel.NormalizeSetting(
                    new ProviderLimitWarningSetting(providerId, fallbackPercent),
                    isUsedPercent);
            LimitWarningProviderSettings.Add(
                new ProviderLimitWarningSettingItemViewModel(
                    setting,
                    providerName,
                    isUsedPercent,
                    displayLanguage));
        }

        // Kept for compatibility with older bindings while the provider-specific rows are used by settings.
        var thresholds = new[] { 10, 20, 30 };
        for (int i = 0; i < thresholds.Length; i++)
        {
            var threshold = thresholds[i];
            var usedThreshold = 100 - threshold;
            var label = AppText.Get(displayLanguage, AppStringKeys.LimitWarningThresholdOption, threshold, usedThreshold);
            var existing = LimitWarningThresholdOptions.FirstOrDefault(option => option.Percent == threshold);
            if (existing is null)
            {
                LimitWarningThresholdOptions.Insert(
                    i,
                    new LimitWarningThresholdOptionViewModel(threshold, label, threshold == selectedThresholdPercent));
            }
            else
            {
                existing.Label = label;
                existing.IsSelected = threshold == selectedThresholdPercent;
            }
        }
    }

    private void ApplyLanguage(AppLanguage language)
    {
        AppTagline = AppText.Get(language, AppStringKeys.AppTagline);
        TrackedLabel = AppText.Get(language, AppStringKeys.TrackedLabel);
        OverallStatusLabel = AppText.Get(language, AppStringKeys.OverallStatusLabel);
        LastCheckedLabel = AppText.Get(language, AppStringKeys.LastCheckedLabel);
        FiveHourModeText = AppText.Get(language, AppStringKeys.FiveHourModeText);
        WeeklyModeText = AppText.Get(language, AppStringKeys.WeeklyModeText);
        BothModeText = AppText.Get(language, AppStringKeys.BothModeText);
        BarsModeText = AppText.Get(language, AppStringKeys.BarsModeText);
        ToggleWidgetButtonText = AppText.Get(language, AppStringKeys.ToggleWidgetButtonText);
        DashboardButtonText = AppText.Get(language, AppStringKeys.DashboardButtonText);
        SettingsButtonText = AppText.Get(language, AppStringKeys.SettingsButtonText);
        SettingsPanelDetailText = AppText.Get(language, AppStringKeys.SettingsPanelDetailText);
        SettingsApplyButtonText = AppText.Get(language, AppStringKeys.SettingsApplyButtonText);
        SettingsCloseButtonText = AppText.Get(language, AppStringKeys.SettingsCloseButtonText);
        SettingsImmediateApplyText = AppText.Get(language, AppStringKeys.SettingsImmediateApplyText);
        SettingsUnsavedTitleText = AppText.Get(language, AppStringKeys.SettingsUnsavedTitleText);
        SettingsUnsavedDetailText = AppText.Get(language, AppStringKeys.SettingsUnsavedDetailText);
        SettingsDiscardButtonText = AppText.Get(language, AppStringKeys.SettingsDiscardButtonText);
        SettingsKeepEditingButtonText = AppText.Get(language, AppStringKeys.SettingsKeepEditingButtonText);
        RefreshButtonText = AppText.Get(language, AppStringKeys.RefreshButtonText);
        RefreshAllButtonText = AppText.Get(language, AppStringKeys.RefreshAllButtonText);
        CopyDiagnosticLogButtonText = AppText.Get(language, AppStringKeys.CopyDiagnosticLogButtonText);
        HideButtonText = AppText.Get(language, AppStringKeys.HideButtonText);
        PinWindowTooltipText = AppText.Get(language, AppStringKeys.PinWindowTooltip);
        ModelSettingsButtonText = AppText.Get(language, AppStringKeys.ModelSettingsButtonText);
        ModelSettingsDetailText = AppText.Get(language, AppStringKeys.ModelSettingsDetailText);
        AccountsButtonText = AppText.Get(language, AppStringKeys.AccountsManagerButtonText);
        LanguageSettingsTitleText = AppText.Get(language, AppStringKeys.LanguageSettingsTitleText);
        LanguageSettingsDetailText = AppText.Get(language, AppStringKeys.LanguageSettingsDetailText);
        ThemeSettingsTitleText = AppText.Get(language, AppStringKeys.ThemeSettingsTitleText);
        ThemeSettingsDetailText = AppText.Get(language, AppStringKeys.ThemeSettingsDetailText);
        ThemeSystemHintText = AppText.Get(language, AppStringKeys.ThemeSystemHintText);
        LimitWarningSettingsTitleText = AppText.Get(language, AppStringKeys.LimitWarningSettingsTitleText);
        LimitWarningSettingsDetailText = AppText.Get(language, AppStringKeys.LimitWarningSettingsDetailText);
        LimitWarningEnabledText = AppText.Get(language, AppStringKeys.LimitWarningEnabledText);
        LimitWarningThresholdText = AppText.Get(language, AppStringKeys.LimitWarningThresholdText);
        CustomThresholdLabelText = AppText.Get(language, AppStringKeys.LimitWarningCustomThresholdLabel);
        InactiveAccountWarningText = AppText.Get(language, AppStringKeys.SettingsInactiveAccountWarning);
        InactiveAccountWarningHintText = AppText.Get(language, AppStringKeys.SettingsInactiveAccountWarningHint);
        UpdateCheckTitleText = AppText.Get(language, AppStringKeys.UpdateCheckTitleText);
        UpdateCheckDetailText = AppText.Get(language, AppStringKeys.UpdateCheckDetailText);
        CheckForUpdatesButtonText = AppText.Get(language, AppStringKeys.CheckForUpdatesButtonText);
        UpdateAvailableTitleText = AppText.Get(language, AppStringKeys.UpdateAvailableTitleText);
        UpdateAvailableConfirmButtonText = AppText.Get(language, AppStringKeys.UpdateAvailableConfirmButtonText);
        UpdateAvailableCancelButtonText = AppText.Get(language, AppStringKeys.UpdateAvailableCancelButtonText);
        UpdateReleaseOpenFailedText = AppText.Get(language, AppStringKeys.UpdateReleaseOpenFailedText);
        DiagnosticLogTitleText = AppText.Get(language, AppStringKeys.DiagnosticLogTitleText);
        DiagnosticLogDetailText = AppText.Get(language, AppStringKeys.DiagnosticLogDetailText);
        AntigravityOAuthTitleText = "Antigravity OAuth";
        AntigravityOAuthDetailText = AntigravityLocalizedText.OAuthDetail(language);
        AntigravityOAuthClientIdLabelText = AntigravityLocalizedText.ClientIdLabel(language);
        AntigravityOAuthClientSecretLabelText = AntigravityLocalizedText.ClientSecretLabel(language);
        AntigravityOAuthSaveButtonText = AppText.Get(language, AppStringKeys.AntigravityOAuthSaveButtonText);
        AntigravityOAuthClearButtonText = AppText.Get(language, AppStringKeys.AntigravityOAuthClearButtonText);
        AntigravityOAuthGuideButtonText = AppText.Get(language, AppStringKeys.AntigravityOAuthGuideButtonText);
        SettingsAntigravityMovedToAccountsText = AppText.Get(language, AppStringKeys.SettingsAntigravityMovedToAccounts);
        TrackProviderLabel = AppText.Get(language, AppStringKeys.TrackProviderLabel);
    }

    private void UpdateAntigravityOAuthActiveClient(
        AntigravityOAuthClientOrigin? origin,
        AppLanguage language)
    {
        if (origin is null)
        {
            AntigravityOAuthActiveClientText = string.Empty;
            return;
        }

        if (origin.Value == AntigravityOAuthClientOrigin.None)
        {
            // "Currently used: none" reads as a contradiction, so spell out what actually
            // happens: the IDE fallback covers reads, only token refresh needs a saved client.
            AntigravityOAuthActiveClientText = AppText.Get(language, AppStringKeys.AntigravityOAuthActiveClientNone);
            return;
        }

        var originLabel = LocalizeOAuthClientOrigin(origin.Value, language);
        var prefix = AppText.Get(language, AppStringKeys.AntigravityOAuthActiveClientLabel);
        AntigravityOAuthActiveClientText = $"{prefix}: {originLabel}";
    }

    internal static string LocalizeOAuthClientOrigin(AntigravityOAuthClientOrigin origin, AppLanguage language)
    {
        return origin switch
        {
            AntigravityOAuthClientOrigin.Environment => AppText.Get(language, AppStringKeys.OAuthClientOriginEnvironment),
            AntigravityOAuthClientOrigin.IdeCredentialFile => AppText.Get(language, AppStringKeys.OAuthClientOriginIde),
            AntigravityOAuthClientOrigin.UserSavedSettings => AppText.Get(language, AppStringKeys.OAuthClientOriginUserSaved),
            _ => AppText.Get(language, AppStringKeys.OAuthClientOriginNone)
        };
    }

    internal static string LocalizeOAuthClientOriginToken(string? token, AppLanguage language)
    {
        var origin = token switch
        {
            "environment" => AntigravityOAuthClientOrigin.Environment,
            "ide" => AntigravityOAuthClientOrigin.IdeCredentialFile,
            "user-saved" => AntigravityOAuthClientOrigin.UserSavedSettings,
            "none" => AntigravityOAuthClientOrigin.None,
            _ => (AntigravityOAuthClientOrigin?)null
        };
        return origin is null ? string.Empty : LocalizeOAuthClientOrigin(origin.Value, language);
    }

    public void SetUpdateCheckStatus(string status)
    {
        UpdateCheckStatusText = status;
        OnPropertyChanged(nameof(UpdateCheckStatusText));
    }

    public void SetUpdateAvailablePrompt(string latestVersion)
    {
        UpdateAvailableMessageText = AppText.Get(
            _displayLanguage,
            AppStringKeys.UpdateAvailableMessageText,
            latestVersion);
        OnPropertyChanged(nameof(UpdateAvailableMessageText));
    }

    public void SetAntigravityOAuthStatus(string status)
    {
        AntigravityOAuthStatusText = status;
        OnPropertyChanged(nameof(AntigravityOAuthStatusText));
    }

    public static string AntigravityOAuthStatusMessage(AppLanguage language, AntigravityOAuthStatusKind status)
    {
        return AntigravityLocalizedText.OAuthStatusMessage(language, status);
    }

    public static string AntigravityOAuthGuideOpenFailedMessage(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.AntigravityOAuthGuideOpenFailed);
    }

    private static UsageWindow? SelectPrimaryWindow(UsageSnapshot snapshot, LimitDisplayMode displayMode)
    {
        if (IsUsedPercentSnapshot(snapshot))
        {
            return snapshot.Windows.OrderByDescending(window => window.PercentRemaining).FirstOrDefault();
        }

        return displayMode switch
        {
            LimitDisplayMode.FiveHourOnly => FindWindow(snapshot, "five-hour"),
            LimitDisplayMode.WeeklyOnly => FindWindow(snapshot, "weekly"),
            _ => snapshot.Windows.OrderBy(w => w.PercentRemaining).FirstOrDefault()
        };
    }

    private static bool IsUsedPercentSnapshot(UsageSnapshot snapshot)
    {
        return snapshot.Windows.Any(UsageWindowLabelText.IsUsedPercent);
    }

    private static string FormatModelCount(int count, AppLanguage language)
    {
        return AppLanguageResolver.Resolve(language) switch
        {
            AppLanguage.Korean => $"{count}개 모델",
            AppLanguage.Japanese => $"{count}件のモデル",
            AppLanguage.Chinese => $"{count} 个模型",
            _ => count == 1 ? "1 model" : $"{count} models"
        };
    }

    private static UsageWindow? FindWindow(UsageSnapshot snapshot, string id)
    {
        return snapshot.Windows.FirstOrDefault(window => window.Id == id)
            ?? snapshot.Windows.FirstOrDefault(window => id == "five-hour" && window.Id.EndsWith("-primary", StringComparison.Ordinal))
            ?? snapshot.Windows.FirstOrDefault(window => id == "weekly" && window.Id.EndsWith("-secondary", StringComparison.Ordinal))
            ?? snapshot.Windows.FirstOrDefault();
    }

    private static string FormatReset(UsageWindow window, AppLanguage language)
    {
        if (!string.IsNullOrWhiteSpace(window.ResetLabel))
        {
            return LocalizeResetLabel(window.ResetLabel!, language);
        }

        return window.ResetAt is null
            ? AppText.Get(language, AppStringKeys.ResetUnavailable)
            : AppText.Get(language, AppStringKeys.ResetAt, window.ResetAt.Value.LocalDateTime);
    }

    private static string FormatDate(DateTimeOffset value, AppLanguage language)
    {
        return language == AppLanguage.Korean
            ? value.LocalDateTime.ToString("yyyy-MM-dd tt h:mm")
            : value.LocalDateTime.ToString("g");
    }

    private static string GetStatusBrush(UsageStatus status, bool isRefreshing)
    {
        if (isRefreshing)
        {
            return BrushKey.StatusRefreshing;
        }

        return status switch
        {
            UsageStatus.Fresh => BrushKey.StatusFresh,
            UsageStatus.Refreshing => BrushKey.StatusRefreshing,
            UsageStatus.Stale => BrushKey.StatusStale,
            UsageStatus.Failed => BrushKey.StatusFailed,
            _ => BrushKey.StatusNeutral
        };
    }

    private static string LocalizeResetLabel(string label, AppLanguage language)
    {
        if (label == "Not active in session")
        {
            return AppText.Get(language, AppStringKeys.NotActiveInSession);
        }

        return label;
    }

    private void ApplyGuidance(IReadOnlyList<UsageSnapshot> snapshots, bool isRefreshing, AppLanguage language)
    {
        if (isRefreshing)
        {
            GuidanceTitle = AppText.Get(language, AppStringKeys.GuidanceRefreshingTitle);
            GuidanceDetail = AppText.Get(language, AppStringKeys.GuidanceRefreshingDetail);
            GuidanceAction = AppText.Get(language, AppStringKeys.GuidanceRefreshingAction);
            WidgetGuidanceText = AppText.Get(language, AppStringKeys.GuidanceRefreshingTitle);
            return;
        }

        if (snapshots.Count == 0)
        {
            GuidanceTitle = AppText.Get(language, AppStringKeys.GuidanceNoDataTitle);
            GuidanceDetail = AppText.Get(language, AppStringKeys.GuidanceNoDataDetail);
            GuidanceAction = AppText.Get(language, AppStringKeys.GuidanceNoDataAction);
            WidgetGuidanceText = AppText.Get(language, AppStringKeys.GuidanceNoDataWidget);
            return;
        }

        if (snapshots.Any(snapshot => snapshot.Status == UsageStatus.Failed))
        {
            GuidanceTitle = AppText.Get(language, AppStringKeys.GuidanceFailedTitle);
            GuidanceDetail = AppText.Get(language, AppStringKeys.GuidanceFailedDetail);
            GuidanceAction = AppText.Get(language, AppStringKeys.GuidanceFailedAction);
            WidgetGuidanceText = AppText.Get(language, AppStringKeys.GuidanceFailedWidget);
            return;
        }

        if (snapshots.Any(snapshot => snapshot.Status == UsageStatus.Stale))
        {
            GuidanceTitle = AppText.Get(language, AppStringKeys.GuidanceStaleTitle);
            GuidanceDetail = AppText.Get(language, AppStringKeys.GuidanceStaleDetail);
            GuidanceAction = AppText.Get(language, AppStringKeys.GuidanceStaleAction);
            WidgetGuidanceText = AppText.Get(language, AppStringKeys.GuidanceStaleWidget);
            return;
        }

        GuidanceTitle = AppText.Get(language, AppStringKeys.GuidanceFreshTitle);
        GuidanceDetail = AppText.Get(language, AppStringKeys.GuidanceFreshDetail);
        GuidanceAction = AppText.Get(language, AppStringKeys.GuidanceFreshAction);
        WidgetGuidanceText = AppText.Get(language, AppStringKeys.GuidanceFreshWidget);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(AppTagline));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(CheckedAtText));
        OnPropertyChanged(nameof(GuidanceTitle));
        OnPropertyChanged(nameof(GuidanceDetail));
        OnPropertyChanged(nameof(GuidanceAction));
        OnPropertyChanged(nameof(WidgetGuidanceText));
        OnPropertyChanged(nameof(TrackedLabel));
        OnPropertyChanged(nameof(OverallStatusLabel));
        OnPropertyChanged(nameof(LastCheckedLabel));
        OnPropertyChanged(nameof(FiveHourModeText));
        OnPropertyChanged(nameof(WeeklyModeText));
        OnPropertyChanged(nameof(BothModeText));
        OnPropertyChanged(nameof(BarsModeText));
        OnPropertyChanged(nameof(ToggleWidgetButtonText));
        OnPropertyChanged(nameof(DashboardButtonText));
        OnPropertyChanged(nameof(SettingsButtonText));
        OnPropertyChanged(nameof(SettingsPanelDetailText));
        OnPropertyChanged(nameof(SettingsApplyButtonText));
        OnPropertyChanged(nameof(SettingsCloseButtonText));
        OnPropertyChanged(nameof(SettingsImmediateApplyText));
        OnPropertyChanged(nameof(SettingsUnsavedTitleText));
        OnPropertyChanged(nameof(SettingsUnsavedDetailText));
        OnPropertyChanged(nameof(SettingsDiscardButtonText));
        OnPropertyChanged(nameof(SettingsKeepEditingButtonText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(RefreshAllButtonText));
        OnPropertyChanged(nameof(CopyDiagnosticLogButtonText));
        OnPropertyChanged(nameof(HideButtonText));
        OnPropertyChanged(nameof(PinWindowTooltipText));
        OnPropertyChanged(nameof(ModelSettingsButtonText));
        OnPropertyChanged(nameof(ModelSettingsDetailText));
        OnPropertyChanged(nameof(AntigravityOAuthTitleText));
        OnPropertyChanged(nameof(AntigravityOAuthDetailText));
        OnPropertyChanged(nameof(AntigravityOAuthClientIdLabelText));
        OnPropertyChanged(nameof(AntigravityOAuthClientSecretLabelText));
        OnPropertyChanged(nameof(AntigravityOAuthSaveButtonText));
        OnPropertyChanged(nameof(AntigravityOAuthClearButtonText));
        OnPropertyChanged(nameof(AntigravityOAuthGuideButtonText));
        OnPropertyChanged(nameof(AntigravityOAuthStatusText));
        OnPropertyChanged(nameof(AntigravityOAuthActiveClientText));
        OnPropertyChanged(nameof(SettingsAntigravityMovedToAccountsText));
        OnPropertyChanged(nameof(LanguageSettingsTitleText));
        OnPropertyChanged(nameof(LanguageSettingsDetailText));
        OnPropertyChanged(nameof(ThemeSettingsTitleText));
        OnPropertyChanged(nameof(ThemeSettingsDetailText));
        OnPropertyChanged(nameof(ThemeSystemHintText));
        OnPropertyChanged(nameof(DashboardOpacityPercent));
        OnPropertyChanged(nameof(WidgetOpacityPercent));
        OnPropertyChanged(nameof(LimitWarningSettingsTitleText));
        OnPropertyChanged(nameof(LimitWarningSettingsDetailText));
        OnPropertyChanged(nameof(LimitWarningEnabledText));
        OnPropertyChanged(nameof(LimitWarningThresholdText));
        OnPropertyChanged(nameof(CustomThresholdLabelText));
        OnPropertyChanged(nameof(IsLimitWarningEnabled));
        OnPropertyChanged(nameof(InactiveAccountWarningText));
        OnPropertyChanged(nameof(InactiveAccountWarningHintText));
        OnPropertyChanged(nameof(IsInactiveAccountWarningEnabled));
        OnPropertyChanged(nameof(LimitWarningThresholdOptions));
        OnPropertyChanged(nameof(UpdateCheckTitleText));
        OnPropertyChanged(nameof(UpdateCheckDetailText));
        OnPropertyChanged(nameof(CheckForUpdatesButtonText));
        OnPropertyChanged(nameof(UpdateCheckStatusText));
        OnPropertyChanged(nameof(UpdateAvailableTitleText));
        OnPropertyChanged(nameof(UpdateAvailableMessageText));
        OnPropertyChanged(nameof(UpdateAvailableConfirmButtonText));
        OnPropertyChanged(nameof(UpdateAvailableCancelButtonText));
        OnPropertyChanged(nameof(UpdateReleaseOpenFailedText));
        OnPropertyChanged(nameof(DiagnosticLogTitleText));
        OnPropertyChanged(nameof(DiagnosticLogDetailText));
        OnPropertyChanged(nameof(TrackProviderLabel));
        OnPropertyChanged(nameof(ProviderSettings));
        OnPropertyChanged(nameof(AccountsButtonText));
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(ThemeOptions));
    }

    public void RaiseColorProperties()
    {
        OnPropertyChanged(nameof(StatusBrush));
        foreach (var provider in Providers)
        {
            provider.RaiseColorProperties();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ProviderSettingItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private IReadOnlyList<ProviderModeOptionViewModel> _modeOptions;
    private string _selectedMode;
    private string _setupHintText;

    public ProviderSettingItemViewModel(ProviderSetting setting, AppLanguage language)
    {
        Id = setting.Id;
        DisplayName = setting.DisplayName;
        _isEnabled = setting.IsEnabled;
        _modeOptions = [];
        _selectedMode = ProviderSetting.NormalizeMode(setting.Id, setting.Mode) ?? "auto";
        _setupHintText = CreateSetupHint(setting.Id, language);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string DisplayName { get; }
    public bool SupportsProfiles => Id == "codex";
    public bool HasModeOptions => ModeOptions.Count > 0;
    public bool HasSetupHint => !string.IsNullOrWhiteSpace(SetupHintText);

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public IReadOnlyList<ProviderModeOptionViewModel> ModeOptions
    {
        get => _modeOptions;
        set
        {
            if (SetField(ref _modeOptions, value))
            {
                OnPropertyChanged(nameof(HasModeOptions));
            }
        }
    }

    public string SelectedMode
    {
        get => _selectedMode;
        set => SetField(ref _selectedMode, ProviderSetting.NormalizeMode(Id, value) ?? "auto");
    }

    public string SetupHintText
    {
        get => _setupHintText;
        set
        {
            if (SetField(ref _setupHintText, value))
            {
                OnPropertyChanged(nameof(HasSetupHint));
            }
        }
    }

    public static string CreateSetupHint(string providerId, AppLanguage language)
    {
        if (providerId == "gemini-pro")
        {
            return AppText.Get(language, AppStringKeys.SetupHintAntigravity);
        }

        if (providerId == "claude")
        {
            return AppText.Get(language, AppStringKeys.SetupHintClaude);
        }

        return string.Empty;
    }

    public static IReadOnlyList<ProviderModeOptionViewModel> CreateModeOptions(string providerId, AppLanguage language)
    {
        return [];
    }

    private static IReadOnlyList<ProviderModeOptionViewModel> CreateReadableCodexModeOptions(AppLanguage language)
    {
        return
        [
            new("auto", AppText.Get(language, AppStringKeys.ModeOptionAuto)),
            new("basic", AppText.Get(language, AppStringKeys.ModeOptionBasic)),
            new("pro", AppText.Get(language, AppStringKeys.ModeOptionProDetailed))
        ];
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

public sealed record ProviderModeOptionViewModel(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record ThemeOptionViewModel(AppThemeMode Mode, string Label, bool IsSelected);

public sealed class LanguageOptionViewModel : INotifyPropertyChanged
{
    private string _code;
    private string _label;
    private bool _isSelected;

    public LanguageOptionViewModel(AppLanguage language, string code, string label, bool isSelected)
    {
        Language = language;
        _code = code;
        _label = label;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language { get; }

    public string Code
    {
        get => _code;
        set => SetField(ref _code, value);
    }

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
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

public enum AntigravityOAuthStatusKind
{
    None,
    MissingInput,
    Saved,
    SaveFailed,
    Cleared,
    ClearFailed,
    SavedExists,
    LoadFailed
}

public sealed class LimitWarningThresholdOptionViewModel : INotifyPropertyChanged
{
    private string _label;
    private bool _isSelected;

    public LimitWarningThresholdOptionViewModel(int percent, string label, bool isSelected)
    {
        Percent = percent;
        _label = label;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Percent { get; }

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
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

public sealed class ProviderLimitWarningSettingItemViewModel : INotifyPropertyChanged
{
    private readonly AppLanguage _language;
    private int _thresholdPercent;
    private string _valueText;

    public ProviderLimitWarningSettingItemViewModel(
        ProviderLimitWarningSetting setting,
        string providerName,
        bool isUsedPercent,
        AppLanguage language)
    {
        ProviderId = setting.ProviderId;
        ProviderName = providerName;
        IsUsedPercent = isUsedPercent;
        _language = language;
        _thresholdPercent = setting.ThresholdPercent;
        IsCustom = setting.IsCustom;
        IsSliderEnabled = setting.IsCustom;
        _valueText = FormatValue(setting.ThresholdPercent, isUsedPercent, language);
        CustomLabel = language switch
        {
            AppLanguage.Korean => "직접 지정",
            AppLanguage.Japanese => "カスタム",
            AppLanguage.Chinese => "自定义",
            _ => "Custom"
        };

        var levels = isUsedPercent
            ? new[] { ("Low", 90), ("Standard", 80), ("Comfortable", 70) }
            : new[] { ("Low", 10), ("Standard", 20), ("Comfortable", 30) };
        foreach (var (level, percent) in levels)
        {
            Recommendations.Add(new LimitWarningRecommendationViewModel(
                ProviderId,
                percent,
                $"{LocalizeLevel(level, language)} · {FormatValue(percent, isUsedPercent, language)}",
                !setting.IsCustom && setting.ThresholdPercent == percent));
        }
    }

    internal static ProviderLimitWarningSetting NormalizeSetting(
        ProviderLimitWarningSetting setting,
        bool isUsedPercent)
    {
        var threshold = Math.Clamp(setting.ThresholdPercent, 1, 99);
        var isRecommended = isUsedPercent
            ? threshold is 90 or 80 or 70
            : threshold is 10 or 20 or 30;
        return setting with
        {
            ThresholdPercent = threshold,
            IsCustom = setting.IsCustom || !isRecommended
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderId { get; }
    public string ProviderName { get; }
    public bool IsUsedPercent { get; }
    public int ThresholdPercent
    {
        get => _thresholdPercent;
        private set
        {
            if (_thresholdPercent == value)
            {
                return;
            }

            _thresholdPercent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThresholdPercent)));
        }
    }
    public bool IsCustom { get; }
    public bool IsSliderEnabled { get; }
    public string ValueText
    {
        get => _valueText;
        private set
        {
            if (_valueText == value)
            {
                return;
            }

            _valueText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
        }
    }
    public string CustomLabel { get; }
    public ObservableCollection<LimitWarningRecommendationViewModel> Recommendations { get; } = [];

    public void SetCustomThreshold(int percent)
    {
        if (!IsCustom)
        {
            return;
        }

        ThresholdPercent = Math.Clamp(percent, 1, 99);
        ValueText = FormatValue(ThresholdPercent, IsUsedPercent, _language);
    }

    private static string FormatValue(int percent, bool isUsedPercent, AppLanguage language)
    {
        if (isUsedPercent)
        {
            return language switch
            {
                AppLanguage.Korean => $"{percent}% 사용 이상",
                AppLanguage.Japanese => $"{percent}% 使用以上",
                AppLanguage.Chinese => $"已用 {percent}% 以上",
                _ => $"{percent}% used or more"
            };
        }

        return language switch
        {
            AppLanguage.Korean => $"남은 {percent}% 이하",
            AppLanguage.Japanese => $"残り {percent}% 以下",
            AppLanguage.Chinese => $"剩余 {percent}% 以下",
            _ => $"{percent}% remaining or less"
        };
    }

    private static string LocalizeLevel(string level, AppLanguage language)
    {
        return (level, language) switch
        {
            ("Low", AppLanguage.Korean) => "낮음",
            ("Standard", AppLanguage.Korean) => "보통",
            ("Comfortable", AppLanguage.Korean) => "여유",
            ("Low", AppLanguage.Japanese) => "低",
            ("Standard", AppLanguage.Japanese) => "標準",
            ("Comfortable", AppLanguage.Japanese) => "余裕",
            ("Low", AppLanguage.Chinese) => "低",
            ("Standard", AppLanguage.Chinese) => "标准",
            ("Comfortable", AppLanguage.Chinese) => "宽松",
            ("Standard", _) => "Standard",
            ("Comfortable", _) => "Comfortable",
            _ => "Low"
        };
    }
}

public sealed record LimitWarningRecommendationViewModel(
    string ProviderId,
    int Percent,
    string Label,
    bool IsSelected);

public sealed class ProviderUsageItemViewModel : INotifyPropertyChanged
{
    private const int CollapsedWindowCount = 4;
    private bool _isExpanded;
    private readonly AppLanguage _language;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProviderUsageItemViewModel(
        UsageSnapshot snapshot,
        LimitDisplayMode displayMode,
        AppLanguage language,
        ProviderSetting? setting = null,
        ProviderAutoRefreshStatus? autoRefreshStatus = null,
        IReadOnlyDictionary<UsageWindowKey, DepletionPrediction>? predictions = null)
    {
        _language = language;
        ProviderName = snapshot.DisplayName;
        ProviderId = snapshot.ProviderId;
        LimitProfileText = CreateLimitProfileText(snapshot, setting, language);
        LimitProfileToolTipText = CreateLimitProfileToolTipText(snapshot, setting, language);
        StatusText = StatusToText(snapshot.Status, language);
        StatusBrush = snapshot.Status == UsageStatus.Failed ? BrushKey.StatusFailed : BrushKey.StatusFresh;
        CheckedAtText = FormatDate(snapshot.CheckedAt, language);
        SourceText = snapshot.Source.ToString();
        LastErrorText = FormatLastError(snapshot, language);
        HasLastErrorText = !string.IsNullOrWhiteSpace(LastErrorText);
        FailureBadgeText = FormatFailureBadge(snapshot, language);
        FailureToolTipText = FormatFailureToolTip(snapshot, language);
        StatusBadgeText = string.IsNullOrWhiteSpace(FailureBadgeText) ? StatusText : FailureBadgeText;
        StatusBadgeToolTipText = FailureToolTipText;
        var failureKind = FailureKindFor(snapshot);
        ShowNoValueBadge = snapshot.Windows.Count == 0
            && failureKind is not ProviderFailureKind.OAuthSetupRequired
            && failureKind is not ProviderFailureKind.AuthRequired
            && failureKind is not ProviderFailureKind.StartOrLoginRequired
            && failureKind is not ProviderFailureKind.InstallOrLoginRequired;
        NoValueBadgeText = AppText.Get(language, AppStringKeys.NoValueBadgeText);
        NoValueBadgeToolTipText = AppText.Get(language, AppStringKeys.NoValueBadgeToolTipText);
        var hasIssue = snapshot.Status == UsageStatus.Failed || snapshot.Windows.Count == 0;
        StatusBadgeForeground = hasIssue ? BrushKey.BadgeFailureFg : BrushKey.BadgeOkFg;
        StatusBadgeBackground = hasIssue ? BrushKey.BadgeFailureBg : BrushKey.BadgeOkBg;
        StatusBadgeBorderBrush = hasIssue ? BrushKey.BadgeFailureBorder : BrushKey.BadgeOkBorder;
        AutoRetryText = FormatAutoRetryText(snapshot, autoRefreshStatus, language);
        ShowAutoRetryText = !string.IsNullOrWhiteSpace(AutoRetryText);
        ShowSourceBadge = snapshot.SourceChannel is not null && snapshot.Status != UsageStatus.Failed;
        SourceBadgeText = snapshot.SourceChannel switch
        {
            "cloud" => AppText.Get(language, AppStringKeys.SourceBadgeCloud),
            "ide-fallback" => AppText.Get(language, AppStringKeys.SourceBadgeIdeFallback),
            _ => string.Empty
        };
        var sourceBaseToolTip = snapshot.SourceChannel switch
        {
            "cloud" => AppText.Get(language, AppStringKeys.SourceBadgeCloudToolTip),
            "ide-fallback" => AppText.Get(language, AppStringKeys.SourceBadgeIdeFallbackToolTip),
            _ => string.Empty
        };
        var originLabel = UsageViewModel.LocalizeOAuthClientOriginToken(snapshot.OAuthClientOrigin, language);
        // When the IDE fallback is actively serving data, token refresh is not needed, so the
        // "no OAuth client (refresh will fail)" note is irrelevant noise — drop it and let the
        // IDE-fallback tooltip convey that the IDE connection is live.
        var suppressOriginNote = snapshot.SourceChannel == "ide-fallback"
            && snapshot.OAuthClientOrigin == "none";
        if (!string.IsNullOrEmpty(originLabel) && !suppressOriginNote)
        {
            var prefix = AppText.Get(language, AppStringKeys.OAuthClientOriginTooltipPrefix);
            SourceBadgeToolTipText = string.IsNullOrEmpty(sourceBaseToolTip)
                ? $"{prefix}: {originLabel}"
                : $"{sourceBaseToolTip}\n{prefix}: {originLabel}";
        }
        else
        {
            SourceBadgeToolTipText = sourceBaseToolTip;
        }
        SourceBadgeForeground = snapshot.SourceChannel == "ide-fallback"
            ? BrushKey.BadgeIdeFg
            : BrushKey.BadgeCloudFg;
        SourceBadgeBackground = snapshot.SourceChannel == "ide-fallback"
            ? BrushKey.BadgeIdeBg
            : BrushKey.BadgeCloudBg;
        SourceBadgeBorderBrush = snapshot.SourceChannel == "ide-fallback"
            ? BrushKey.BadgeIdeBorder
            : BrushKey.BadgeCloudBorder;
        DisplayMode = displayMode;
        ShowBars = displayMode == LimitDisplayMode.Bars;
        BrandColorBrush = ProviderBrandColor(snapshot.ProviderId);

        var visibleWindows = SelectVisibleWindows(snapshot, displayMode).ToList();
        var usesUsedPercent = IsUsedPercentSnapshot(snapshot);
        var hasAntigravityWindows = visibleWindows.Any(
            w => w.Id.StartsWith("antigravity-", StringComparison.Ordinal));

        // Keep window order consistent across providers: five-hour first, then weekly, then the rest.
        // Antigravity surfaces per-model windows, which stay ordered by most-used first.
        var orderedWindows = hasAntigravityWindows
            ? visibleWindows.OrderByDescending(window => window.PercentRemaining).ToList()
            : visibleWindows.OrderBy(WindowSortRank).ToList();
        var primary = orderedWindows.FirstOrDefault();

        ShowPrimarySummary = !usesUsedPercent || !hasAntigravityWindows;
        PrimaryPercent = primary?.PercentRemaining ?? 0;
        UrgencyBrush = primary is null
            ? BrushKey.StatusNeutral
            : ComputeUrgencyBrush(PrimaryPercent, usesUsedPercent);
        PrimaryPercentText = primary is null
            ? "--"
            : usesUsedPercent
                ? AppText.Get(language, AppStringKeys.PercentUsedValue, primary.PercentRemaining)
                : AppText.Get(language, AppStringKeys.PercentLeftValue, primary.PercentRemaining);
        PrimaryLabel = primary is null
            ? AppText.Get(language, AppStringKeys.PrimaryNoWindow)
            : usesUsedPercent
                ? UsageWindowLabelText.ShortFor(primary, language)
                : UsageWindowLabelText.For(primary, language);
        ResetText = primary is null
            ? AppText.Get(language, AppStringKeys.PrimaryNoReset)
            : usesUsedPercent && string.IsNullOrWhiteSpace(primary.ResetLabel) && primary.ResetAt is null
                ? string.Empty
                : FormatReset(primary, language);
        ShowPrimaryBar = displayMode == LimitDisplayMode.Bars && primary is not null;
        ShowPrimaryProgress = ShowPrimarySummary && ShowPrimaryBar;
        DepletionPrediction? primaryPrediction = null;
        if (primary is not null)
        {
            predictions?.TryGetValue(new UsageWindowKey(snapshot.ProviderId, primary.Id), out primaryPrediction);
        }

        ApplyPrediction(
            primaryPrediction,
            primary,
            language,
            out var predictionText,
            out var predictionDetailText,
            out var sparklinePoints,
            out var sparklineProjectionPoints);
        ShowPrediction = ShowPrimarySummary
            && primaryPrediction?.State is
                PredictionState.Collecting or PredictionState.WaitingForChange or PredictionState.Depleted
                or PredictionState.WillDeplete or PredictionState.ResetsFirst;
        PredictionText = predictionText;
        PredictionDetailText = predictionDetailText;
        SparklinePoints = sparklinePoints;
        SparklineProjectionPoints = sparklineProjectionPoints;

        if (displayMode is LimitDisplayMode.BothText or LimitDisplayMode.Bars)
        {
            var detailWindows = hasAntigravityWindows
                ? orderedWindows
                : orderedWindows.Where(w => w.Id != primary?.Id);
            foreach (var window in detailWindows)
            {
                DepletionPrediction? windowPrediction = null;
                predictions?.TryGetValue(
                    new UsageWindowKey(snapshot.ProviderId, window.Id),
                    out windowPrediction);
                Windows.Add(new UsageWindowItemViewModel(
                    window,
                    displayMode == LimitDisplayMode.Bars,
                    language,
                    windowPrediction));
            }
        }

        HiddenWindowHintText = FormatHiddenWindowHintText(Windows.Count - CollapsedWindowCount, language);
        UpdateVisibleWindows();

        var resetCredits = snapshot.ResetCredits;
        ShowResetCredits = resetCredits is { AvailableCount: > 0 };
        var resetExpiryImminent = ShowResetCredits
            && IsResetExpiryImminent(resetCredits!.NearestExpiry, DateTimeOffset.Now);
        ResetCreditText = ShowResetCredits
            ? AppText.Get(
                language,
                resetExpiryImminent
                    ? AppStringKeys.ResetCreditSummaryImminent
                    : AppStringKeys.ResetCreditSummaryNormal,
                resetCredits!.AvailableCount)
            : string.Empty;
        ResetCreditBrush = resetExpiryImminent ? BrushKey.StatusWarning : BrushKey.TextSecondary;
        ResetCreditToolTipText = ShowResetCredits && resetCredits!.NearestExpiry is { } expiry
            ? AppText.Get(language, AppStringKeys.ResetCreditExpiryTooltip, expiry.LocalDateTime)
            : string.Empty;
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            UpdateVisibleWindows();
            OnPropertyChanged(nameof(ExpandButtonText));
        }
    }

    public bool ShowExpandToggle => Windows.Count > CollapsedWindowCount;

    public string ExpandButtonText => FormatExpandButtonText(Windows.Count - CollapsedWindowCount, _isExpanded, _language);

    public string HiddenWindowHintText { get; private set; } = string.Empty;

    public void ToggleExpand() => IsExpanded = !IsExpanded;

    public void RaiseColorProperties()
    {
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusBadgeForeground));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeBorderBrush));
        OnPropertyChanged(nameof(SourceBadgeForeground));
        OnPropertyChanged(nameof(SourceBadgeBackground));
        OnPropertyChanged(nameof(SourceBadgeBorderBrush));
        OnPropertyChanged(nameof(UrgencyBrush));
        OnPropertyChanged(nameof(BrandColorBrush));
        OnPropertyChanged(nameof(ResetCreditBrush));
        foreach (var window in Windows)
        {
            window.RaiseColorProperties();
        }
    }

    private void UpdateVisibleWindows()
    {
        VisibleWindows.Clear();
        var items = _isExpanded || Windows.Count <= CollapsedWindowCount
            ? (IEnumerable<UsageWindowItemViewModel>)Windows
            : Windows.Take(CollapsedWindowCount);
        foreach (var item in items)
        {
            VisibleWindows.Add(item);
        }
    }

    private static string FormatExpandButtonText(int hiddenCount, bool isExpanded, AppLanguage language)
    {
        if (isExpanded)
        {
            return language switch
            {
                AppLanguage.Korean => "접기",
                AppLanguage.Japanese => "折りたたむ",
                AppLanguage.Chinese => "收起",
                _ => "Show less"
            };
        }

        return language switch
        {
            AppLanguage.Korean => $"더 보기 ({hiddenCount}개)",
            AppLanguage.Japanese => $"さらに表示 ({hiddenCount}件)",
            AppLanguage.Chinese => $"更多 ({hiddenCount}个)",
            _ => hiddenCount == 1 ? "Show 1 more" : $"Show {hiddenCount} more"
        };
    }

    private static string FormatHiddenWindowHintText(int hiddenCount, AppLanguage language)
    {
        return language switch
        {
            AppLanguage.Korean => $"+{hiddenCount}개 더 · 대시보드에서 확인",
            AppLanguage.Japanese => $"+{hiddenCount}件 · ダッシュボードで確認",
            AppLanguage.Chinese => $"+{hiddenCount}个 · 在仪表板中查看",
            _ => hiddenCount == 1 ? "+1 more · View in dashboard" : $"+{hiddenCount} more · View in dashboard"
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string ProviderName { get; }
    public string ProviderId { get; }
    public string LimitProfileText { get; }
    public string LimitProfileToolTipText { get; }
    public string StatusText { get; }
    public string StatusBrush { get; }
    public string CheckedAtText { get; }
    public string SourceText { get; }
    public string LastErrorText { get; }
    public bool HasLastErrorText { get; }
    public string FailureBadgeText { get; }
    public string FailureToolTipText { get; }
    public string StatusBadgeText { get; }
    public string StatusBadgeToolTipText { get; }
    public string NoValueBadgeText { get; }
    public string NoValueBadgeToolTipText { get; }
    public bool ShowNoValueBadge { get; }
    public string StatusBadgeForeground { get; }
    public string StatusBadgeBackground { get; }
    public string StatusBadgeBorderBrush { get; }
    public string AutoRetryText { get; }
    public bool ShowAutoRetryText { get; }
    public bool ShowSourceBadge { get; }
    public string SourceBadgeText { get; }
    public string SourceBadgeToolTipText { get; }
    public string SourceBadgeForeground { get; }
    public string SourceBadgeBackground { get; }
    public string SourceBadgeBorderBrush { get; }
    public int PrimaryPercent { get; }
    public string PrimaryPercentText { get; }
    public string PrimaryLabel { get; }
    public string ResetText { get; }
    public string UrgencyBrush { get; }
    public bool ShowResetCredits { get; }
    public string ResetCreditText { get; }
    public string ResetCreditBrush { get; }
    public string ResetCreditToolTipText { get; }
    public LimitDisplayMode DisplayMode { get; }
    public bool ShowBars { get; }
    public bool ShowPrimarySummary { get; }
    public bool ShowPrimaryBar { get; }
    public bool ShowPrimaryProgress { get; }
    public bool ShowPrediction { get; }
    public string PredictionText { get; }
    public string PredictionDetailText { get; }
    public string SparklinePoints { get; }
    public string SparklineProjectionPoints { get; }
    public string BrandColorBrush { get; }
    public ObservableCollection<UsageWindowItemViewModel> Windows { get; } = [];
    public ObservableCollection<UsageWindowItemViewModel> VisibleWindows { get; } = [];

    internal static bool IsResetExpiryImminent(DateTimeOffset? nearestExpiry, DateTimeOffset now)
    {
        return nearestExpiry is { } expiry && expiry - now <= TimeSpan.FromDays(7);
    }

    internal static string ComputeUrgencyBrush(int percent, bool isUsedPercent)
    {
        var risk = isUsedPercent ? percent : 100 - percent;
        return risk switch
        {
            >= 90 => BrushKey.UrgencyCritical,
            >= 80 => BrushKey.UrgencyHigh,
            >= 50 => BrushKey.UrgencyMedium,
            _ => BrushKey.UrgencyLow
        };
    }

    internal static void ApplyPrediction(
        DepletionPrediction? prediction,
        UsageWindow? window,
        AppLanguage language,
        out string predictionText,
        out string predictionDetailText,
        out string sparklinePoints,
        out string sparklineProjectionPoints)
    {
        predictionText = string.Empty;
        predictionDetailText = string.Empty;
        sparklinePoints = string.Empty;
        sparklineProjectionPoints = string.Empty;
        if (prediction?.State is not (
            PredictionState.Collecting or PredictionState.WaitingForChange or PredictionState.Depleted
            or PredictionState.WillDeplete or PredictionState.ResetsFirst))
        {
            return;
        }

        predictionText = prediction.State switch
        {
            PredictionState.Collecting => AppText.Get(
                language,
                AppStringKeys.PredictionCollecting,
                Math.Min(prediction.TrendSamples.Count, DepletionPredictor.MinimumSampleCount),
                DepletionPredictor.MinimumSampleCount),
            PredictionState.WaitingForChange => AppText.Get(
                language,
                AppStringKeys.PredictionWaitingForChange),
            PredictionState.Depleted => AppText.Get(language, AppStringKeys.PredictionDepleted),
            PredictionState.WillDeplete => AppText.Get(
                language,
                AppStringKeys.PredictionDepleteIn,
                FormatPredictionDuration(prediction.TimeRemaining ?? TimeSpan.Zero, language)),
            _ => AppText.Get(language, NoDepletionKeyFor(window))
        };
        if (prediction.State == PredictionState.WillDeplete && prediction.DepletionAt is not null)
        {
            var depletionAt = prediction.DepletionAt.Value;
            var localDepletionAt = depletionAt.LocalDateTime;
            object depletionAtValue = DepletesOnLaterDay(depletionAt, prediction.TimeRemaining)
                ? FormatPredictionDateTime(localDepletionAt, language)
                : localDepletionAt;
            predictionDetailText = AppText.Get(
                language,
                AppStringKeys.PredictionDepletionAt,
                depletionAtValue);
        }

        (sparklinePoints, sparklineProjectionPoints) = BuildSparklinePoints(prediction);
    }

    private static string NoDepletionKeyFor(UsageWindow? window)
    {
        var id = window?.Id;
        if (string.IsNullOrEmpty(id))
        {
            return AppStringKeys.PredictionNoDepletion;
        }
        if (id.Equals("five-hour", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("-primary", StringComparison.OrdinalIgnoreCase))
        {
            return AppStringKeys.PredictionNoDepletionFiveHour;
        }
        if (id.StartsWith("weekly", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("-secondary", StringComparison.OrdinalIgnoreCase))
        {
            return AppStringKeys.PredictionNoDepletionWeekly;
        }
        return AppStringKeys.PredictionNoDepletion;
    }

    // Show the calendar date (not just the clock time) whenever depletion lands on a
    // different day than now. A bare time like "오전 1:00" is ambiguous both for multi-day
    // ETAs and for sub-24h windows that cross midnight (e.g. 23:00 now -> 01:00 tomorrow).
    private static bool DepletesOnLaterDay(DateTimeOffset depletionAt, TimeSpan? timeRemaining)
    {
        if (timeRemaining is not { } remaining)
        {
            return false;
        }

        var now = depletionAt - remaining;
        return depletionAt.LocalDateTime.Date != now.LocalDateTime.Date;
    }

    private static string FormatPredictionDateTime(DateTime dateTime, AppLanguage language)
    {
        return AppLanguageResolver.Resolve(language) switch
        {
            AppLanguage.Korean => dateTime.ToString(
                "M월 d일 tt h:mm",
                CultureInfo.GetCultureInfo("ko")),
            AppLanguage.Japanese => dateTime.ToString(
                "M月d日 H:mm",
                CultureInfo.GetCultureInfo("ja")),
            AppLanguage.Chinese => dateTime.ToString(
                "M月d日 H:mm",
                CultureInfo.GetCultureInfo("zh-Hans")),
            _ => dateTime.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)
        };
    }

    private static string FormatPredictionDuration(TimeSpan duration, AppLanguage language)
    {
        var totalMinutes = Math.Max(1, (int)Math.Round(duration.TotalMinutes));
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes % (24 * 60) / 60;
        var minutes = totalMinutes % 60;
        return AppLanguageResolver.Resolve(language) switch
        {
            AppLanguage.Korean when days > 0 => $"{days}일 {hours}시간",
            AppLanguage.Korean when hours > 0 => $"{hours}시간 {minutes}분",
            AppLanguage.Korean => $"{minutes}분",
            AppLanguage.Japanese when days > 0 => $"{days}日 {hours}時間",
            AppLanguage.Japanese when hours > 0 => $"{hours}時間 {minutes}分",
            AppLanguage.Japanese => $"{minutes}分",
            AppLanguage.Chinese when days > 0 => $"{days}天 {hours}小时",
            AppLanguage.Chinese when hours > 0 => $"{hours}小时 {minutes}分钟",
            AppLanguage.Chinese => $"{minutes}分钟",
            _ when days > 0 => $"{days}d {hours}h",
            _ when hours > 0 => $"{hours}h {minutes}m",
            _ => $"{minutes}m"
        };
    }

    private static (string History, string Projection) BuildSparklinePoints(DepletionPrediction prediction)
    {
        const double width = 240;
        const double height = 44;
        const double padding = 2;
        var samples = prediction.TrendSamples.OrderBy(sample => sample.AtUtc).ToList();
        if (samples.Count < 2)
        {
            return (string.Empty, string.Empty);
        }

        var start = samples[0].AtUtc;
        var last = samples[^1];
        var end = prediction.State == PredictionState.WillDeplete && prediction.DepletionAt > last.AtUtc
            ? prediction.DepletionAt.Value
            : last.AtUtc;
        var totalSeconds = Math.Max(1, (end - start).TotalSeconds);
        double X(DateTimeOffset at) =>
            padding + Math.Clamp((at - start).TotalSeconds / totalSeconds, 0, 1) * (width - padding * 2);
        double Y(double consumed) =>
            height - padding - Math.Clamp(consumed, 0, 100) / 100 * (height - padding * 2);
        string Point(double x, double y) =>
            $"{x.ToString("0.##", CultureInfo.InvariantCulture)},{y.ToString("0.##", CultureInfo.InvariantCulture)}";

        var history = string.Join(" ", samples.Select(sample => Point(X(sample.AtUtc), Y(sample.ConsumedPercent))));
        var projection = prediction.State == PredictionState.WillDeplete && prediction.DepletionAt is not null
            ? string.Join(
                " ",
                Point(X(last.AtUtc), Y(last.ConsumedPercent)),
                Point(X(prediction.DepletionAt.Value), Y(100)))
            : string.Empty;
        return (history, projection);
    }

    private static int WindowSortRank(UsageWindow window)
    {
        if (window.Id == "five-hour")
        {
            return 0;
        }

        if (window.Id == "weekly" || window.Id.StartsWith("weekly-", StringComparison.Ordinal))
        {
            return 1;
        }

        return 2;
    }

    private static IEnumerable<UsageWindow> SelectVisibleWindows(UsageSnapshot snapshot, LimitDisplayMode displayMode)
    {
        if (IsUsedPercentSnapshot(snapshot))
        {
            return snapshot.Windows;
        }

        return displayMode switch
        {
            LimitDisplayMode.FiveHourOnly => SelectSingle(snapshot, "five-hour"),
            LimitDisplayMode.WeeklyOnly => SelectSingle(snapshot, "weekly"),
            _ => snapshot.Windows
        };
    }

    private static IEnumerable<UsageWindow> SelectSingle(UsageSnapshot snapshot, string id)
    {
        var window = snapshot.Windows.FirstOrDefault(item => id == "five-hour" && item.Id.EndsWith("-primary", StringComparison.Ordinal))
            ?? snapshot.Windows.FirstOrDefault(item => id == "weekly" && item.Id.EndsWith("-secondary", StringComparison.Ordinal))
            ?? snapshot.Windows.FirstOrDefault(item => item.Id == id)
            ?? snapshot.Windows.FirstOrDefault();
        return window is null ? [] : [window];
    }

    private static string CreateLimitProfileText(UsageSnapshot snapshot, ProviderSetting? setting, AppLanguage language)
    {
        if (snapshot.ProviderId == "codex")
        {
            return AppText.Get(language, AppStringKeys.LimitProfileCodex);
        }

        if (snapshot.ProviderId == "claude")
        {
            return AppText.Get(language, AppStringKeys.LimitProfileClaude);
        }

        if (snapshot.ProviderId == "gemini-pro")
        {
            return AppText.Get(language, AppStringKeys.LimitProfileAntigravity);
        }

        return AppText.Get(language, AppStringKeys.LimitProfileDefault);
    }

    private static string CreateLimitProfileToolTipText(UsageSnapshot snapshot, ProviderSetting? setting, AppLanguage language)
    {
        if (snapshot.ProviderId == "codex")
        {
            return AppText.Get(language, AppStringKeys.LimitProfileToolTipCodex);
        }

        if (snapshot.ProviderId == "claude" || snapshot.ProviderId.StartsWith("claude-", StringComparison.Ordinal))
        {
            return AppText.Get(language, AppStringKeys.LimitProfileToolTipClaude);
        }

        if (snapshot.ProviderId == "gemini-pro")
        {
            return AppText.Get(language, AppStringKeys.LimitProfileToolTipAntigravity);
        }

        return AppText.Get(language, AppStringKeys.LimitProfileToolTipDefault);
    }

    private static bool IsUsedPercentSnapshot(UsageSnapshot snapshot)
    {
        return snapshot.Windows.Any(UsageWindowLabelText.IsUsedPercent);
    }

    private static string ProviderBrandColor(string providerId)
    {
        if (providerId == "claude" || providerId.StartsWith("claude-", StringComparison.Ordinal))
            return BrushKey.BrandClaude;
        if (providerId == "gemini-pro")
            return BrushKey.BrandAntigravity;
        if (providerId == "codex")
            return BrushKey.BrandCodex;
        return BrushKey.BrandUnknown;
    }

    private static string FormatReset(UsageWindow window, AppLanguage language)
    {
        if (!string.IsNullOrWhiteSpace(window.ResetLabel))
        {
            return LocalizeResetLabel(window.ResetLabel!, language);
        }

        return window.ResetAt is null
            ? AppText.Get(language, AppStringKeys.ResetUnavailable)
            : AppText.Get(language, AppStringKeys.ResetAt, window.ResetAt.Value.LocalDateTime);
    }

    private static string StatusToText(UsageStatus status, AppLanguage language)
    {
        return status switch
        {
            UsageStatus.Fresh => AppText.Get(language, AppStringKeys.ProviderStatusFresh),
            UsageStatus.Refreshing => AppText.Get(language, AppStringKeys.ProviderStatusRefreshing),
            UsageStatus.Stale => AppText.Get(language, AppStringKeys.ProviderStatusStale),
            UsageStatus.Failed => AppText.Get(language, AppStringKeys.ProviderStatusFailed),
            _ => status.ToString()
        };
    }

    private static string FormatLastError(UsageSnapshot snapshot, AppLanguage language)
    {
        if (snapshot.ProviderId == "gemini-pro"
            && !string.IsNullOrWhiteSpace(snapshot.CloudFailureSummary)
            && !string.IsNullOrWhiteSpace(snapshot.IdeFailureSummary))
        {
            var cloudPrefix = AppText.Get(language, AppStringKeys.CloudPrefix);
            var idePrefix = AppText.Get(language, AppStringKeys.IdeFallbackPrefix);
            var cloudCause = LocalizeAntigravityFailureCause(snapshot.CloudFailureSummary!, language);
            var ideCause = LocalizeAntigravityFailureCause(snapshot.IdeFailureSummary!, language);
            return $"{cloudPrefix}: {cloudCause}\n{idePrefix}: {ideCause}";
        }

        if (string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            return string.Empty;
        }

        if ((snapshot.ProviderId == "claude" || snapshot.ProviderId.StartsWith("claude-", StringComparison.Ordinal))
            && snapshot.LastError.Contains("Claude OAuth credentials were not found", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.LastErrorClaudeInstallLogin);
        }

        if (snapshot.ProviderId == "gemini-pro"
            && IsAntigravityOAuthClientMissing(snapshot.LastError))
        {
            return FormatAntigravityMissingOAuthClientError(language);
        }

        if (snapshot.Status == UsageStatus.Failed)
        {
            return FormatGenericLastError(snapshot, language);
        }

        return snapshot.LastError;
    }

    private static bool IsAntigravityOAuthClientMissing(string? error)
    {
        return ContainsAny(
            error ?? string.Empty,
            "no OAuth client secret is available",
            "OAuth client secret was not found",
            "OAuth client values were not found");
    }

    private static string FormatAntigravityMissingOAuthClientError(AppLanguage language)
    {
        return AntigravityLocalizedText.MissingOAuthClientError(language);
    }

    private static string LocalizeAntigravityFailureCause(string cause, AppLanguage language)
    {
        var normalized = cause.Trim();
        if (normalized.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseHttp401);
        }

        if (normalized.Equals("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseTimeout);
        }

        if (normalized.Equals("no endpoint discovered", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseNoEndpoint);
        }

        if (normalized.Contains("endpoint reachable", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseEndpointNoQuota);
        }

        if (normalized.Contains("installation not detected", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseInstallNotDetected);
        }

        if (normalized.Contains("oauth client secret missing", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("no OAuth client secret is available", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("OAuth client values were not found", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseOAuthClientSecretMissing);
        }

        if (normalized.Contains("OAuth client ID was not found", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseOAuthClientIdMissing);
        }

        if (normalized.Contains("sign in required", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("OAuth credentials were not found", StringComparison.OrdinalIgnoreCase))
        {
            return AppText.Get(language, AppStringKeys.AntigravityCauseSignInRequired);
        }

        return normalized;
    }

    private static string FormatGenericLastError(UsageSnapshot snapshot, AppLanguage language)
    {
        return FailureKindFor(snapshot) switch
        {
            ProviderFailureKind.Timeout => snapshot.ProviderId == "gemini-pro"
                ? FormatAntigravityTimeoutLastError(language)
                : FormatTimeoutLastError(language),
            ProviderFailureKind.Network => AppText.Get(language, AppStringKeys.LastErrorNetwork),
            ProviderFailureKind.Parse => AppText.Get(language, AppStringKeys.LastErrorParse),
            ProviderFailureKind.IdeUnavailable when snapshot.ProviderId == "gemini-pro" =>
                FormatAntigravityNoQuotaLastError(snapshot.LastError, language),
            ProviderFailureKind.NoQuota => snapshot.ProviderId == "gemini-pro"
                ? FormatAntigravityNoQuotaLastError(snapshot.LastError, language)
                : AppText.Get(language, AppStringKeys.LastErrorNoQuotaGeneric),
            _ => FormatFailureToolTip(snapshot, language)
        };
    }

    private static string FormatAntigravityNoQuotaLastError(string? error, AppLanguage language)
    {
        if (ContainsAny(error ?? string.Empty, "IDE fallback was not available", "no endpoint was discovered"))
        {
            return AppText.Get(language, AppStringKeys.LastErrorNoQuotaAntigravityNoEndpoint);
        }

        if (ContainsAny(error ?? string.Empty, "IDE fallback was attempted", "returned no quota buckets"))
        {
            return AppText.Get(language, AppStringKeys.LastErrorNoQuotaAntigravityIdeAttempted);
        }

        return AppText.Get(language, AppStringKeys.LastErrorNoQuotaAntigravityDefault);
    }

    private static string FormatTimeoutLastError(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.LastErrorTimeout);
    }

    private static string FormatAntigravityTimeoutLastError(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.LastErrorAntigravityTimeout);
    }

    private static string FormatFailureBadge(UsageSnapshot snapshot, AppLanguage language)
    {
        if (snapshot.Windows.Count == 0 && string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            return AppText.Get(language, AppStringKeys.NoValueBadgeText);
        }

        if (snapshot.Status != UsageStatus.Failed)
        {
            return string.Empty;
        }

        return FailureKindFor(snapshot) switch
        {
            ProviderFailureKind.OAuthSetupRequired => AppText.Get(language, AppStringKeys.BadgeOAuthSetupRequired),
            ProviderFailureKind.AuthRequired => AppText.Get(language, AppStringKeys.BadgeAuthRequired),
            ProviderFailureKind.StartOrLoginRequired => AppText.Get(language, AppStringKeys.BadgeStartLoginRequired),
            ProviderFailureKind.InstallOrLoginRequired => AppText.Get(language, AppStringKeys.BadgeInstallLoginRequired),
            ProviderFailureKind.Timeout => AppText.Get(language, AppStringKeys.BadgeTimedOut),
            ProviderFailureKind.Network => AppText.Get(language, AppStringKeys.BadgeNetworkIssue),
            ProviderFailureKind.Parse => AppText.Get(language, AppStringKeys.BadgeParseFailed),
            ProviderFailureKind.IdeUnavailable => AppText.Get(language, AppStringKeys.BadgeIdeUnavailable),
            ProviderFailureKind.NoQuota => AppText.Get(language, AppStringKeys.BadgeNoQuotaData),
            _ => AppText.Get(language, AppStringKeys.BadgeNeedsReview)
        };
    }

    private static string FormatAutoRetryText(
        UsageSnapshot snapshot,
        ProviderAutoRefreshStatus? autoRefreshStatus,
        AppLanguage language)
    {
        if (snapshot.Status != UsageStatus.Failed || autoRefreshStatus?.NextAutomaticRefreshAt is not { } next)
        {
            return string.Empty;
        }

        var remaining = next - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return AppText.Get(language, AppStringKeys.AutoRetryIn, minutes);
    }

    private static string FormatFailureToolTip(UsageSnapshot snapshot, AppLanguage language)
    {
        if (snapshot.Windows.Count == 0 && string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            return AppText.Get(language, AppStringKeys.ToolTipNoValue);
        }

        if (snapshot.Status != UsageStatus.Failed)
        {
            return string.Empty;
        }

        return FailureKindFor(snapshot) switch
        {
            ProviderFailureKind.OAuthSetupRequired when snapshot.ProviderId == "gemini-pro" =>
                AppText.Get(language, AppStringKeys.ToolTipOAuthSetupRequired),
            ProviderFailureKind.Timeout when snapshot.ProviderId == "gemini-pro" =>
                AppText.Get(language, AppStringKeys.ToolTipCloudTimeoutAntigravity),
            ProviderFailureKind.IdeUnavailable when snapshot.ProviderId == "gemini-pro" =>
                FormatAntigravityIdeUnavailableToolTip(snapshot.LastError, language),
            ProviderFailureKind.NoQuota when snapshot.ProviderId == "gemini-pro" =>
                FormatAntigravityNoQuotaLastError(snapshot.LastError, language),
            ProviderFailureKind.AuthRequired => AppText.Get(language, AppStringKeys.ToolTipAuthRequired),
            ProviderFailureKind.StartOrLoginRequired when snapshot.ProviderId == "gemini-pro" =>
                AppText.Get(language, AppStringKeys.ToolTipAntigravityStartLogin),
            ProviderFailureKind.InstallOrLoginRequired when snapshot.ProviderId == "codex" =>
                AppText.Get(language, AppStringKeys.ToolTipCodexInstallLogin),
            ProviderFailureKind.InstallOrLoginRequired when snapshot.ProviderId == "gemini-pro" =>
                AppText.Get(language, AppStringKeys.ToolTipAntigravityInstallLogin),
            ProviderFailureKind.InstallOrLoginRequired when snapshot.ProviderId == "claude" || snapshot.ProviderId.StartsWith("claude-", StringComparison.Ordinal) =>
                AppText.Get(language, AppStringKeys.ToolTipClaudeInstallLogin),
            _ => AppText.Get(language, AppStringKeys.ToolTipGenericFailure)
        };
    }

    private static string FormatAntigravityIdeUnavailableToolTip(string? error, AppLanguage language)
    {
        if (!ContainsAny(
                error ?? string.Empty,
                "Antigravity quota was not available",
                "Last cloud error",
                "Cloud"))
        {
            return AppText.Get(language, AppStringKeys.ToolTipIdeUnavailableNoCloud);
        }

        return AppText.Get(language, AppStringKeys.ToolTipIdeUnavailableCloudFailed);
    }

    private static ProviderFailureKind FailureKindFor(UsageSnapshot snapshot)
    {
        var error = snapshot.LastError ?? string.Empty;
        if (string.IsNullOrWhiteSpace(error))
        {
            return ProviderFailureKind.Unknown;
        }

        if (snapshot.ProviderId == "gemini-pro"
            && ContainsAny(
                error,
                "Antigravity installation was not found",
                "Install Antigravity",
                "Antigravity is not installed"))
        {
            return ProviderFailureKind.InstallOrLoginRequired;
        }

        if (snapshot.ProviderId == "gemini-pro"
            && ContainsAny(
                error,
                "IDE fallback was not available",
                "no endpoint was discovered",
                "IDE endpoint discovery returned no candidates"))
        {
            return ProviderFailureKind.StartOrLoginRequired;
        }

        if (snapshot.ProviderId == "gemini-pro"
            && IsAntigravityOAuthClientMissing(error))
        {
            return ProviderFailureKind.OAuthSetupRequired;
        }

        if (snapshot.ProviderId == "gemini-pro"
            && ContainsAny(
                error,
                "Antigravity quota was not available"))
        {
            return ProviderFailureKind.NoQuota;
        }

        if (snapshot.ProviderId == "gemini-pro"
            && ContainsAny(
                error,
                "Google Antigravity OAuth credentials were not found",
                "Run Antigravity login first",
                "sign in to Antigravity",
                "Antigravity Cloud setup"))
        {
            return ProviderFailureKind.AuthRequired;
        }

        if (snapshot.ProviderId == "codex"
            && ContainsAny(
                error,
                "timed out",
                "timeout",
                "app-server closed stdout",
                "Could not start Codex app-server",
                "Codex OAuth token was not found",
                "codex was not found",
                "codex command was not found",
                "codex.cmd",
                "spawn codex",
                "ENOENT"))
        {
            return ProviderFailureKind.InstallOrLoginRequired;
        }

        if ((snapshot.ProviderId == "claude" || snapshot.ProviderId.StartsWith("claude-", StringComparison.Ordinal))
            && ContainsAny(
                error,
                "credentials were not found",
                "Run Claude Code login first",
                "Claude Code was not found",
                "Claude command was not found",
                "claude was not found",
                "claude not found",
                "ENOENT"))
        {
            return ProviderFailureKind.InstallOrLoginRequired;
        }

        if (ContainsAny(error, "not installed", "install", "setup"))
        {
            return ProviderFailureKind.InstallOrLoginRequired;
        }

        if (ContainsAny(error, "credential", "oauth", "unauthorized", "sign in", "login expired", "login first", "auth"))
        {
            return ProviderFailureKind.AuthRequired;
        }

        if (ContainsAny(error, "timed out", "timeout"))
        {
            return ProviderFailureKind.Timeout;
        }

        if (ContainsAny(error, "network", "connection", "dns", "socket", "http"))
        {
            return ProviderFailureKind.Network;
        }

        if (ContainsAny(error, "json", "parse", "deserialize"))
        {
            return ProviderFailureKind.Parse;
        }

        if (ContainsAny(error, "no rate-limit windows", "no quota", "no usage", "returned no"))
        {
            return ProviderFailureKind.NoQuota;
        }

        return ProviderFailureKind.Unknown;
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatDate(DateTimeOffset value, AppLanguage language)
    {
        return language == AppLanguage.Korean
            ? value.LocalDateTime.ToString("yyyy-MM-dd tt h:mm")
            : value.LocalDateTime.ToString("g");
    }

    private static string LocalizeResetLabel(string label, AppLanguage language)
    {
        if (label == "Not active in session")
        {
            return AppText.Get(language, AppStringKeys.NotActiveInSession);
        }

        return label;
    }
}

internal enum ProviderFailureKind
{
    OAuthSetupRequired,
    AuthRequired,
    StartOrLoginRequired,
    InstallOrLoginRequired,
    Timeout,
    Network,
    Parse,
    IdeUnavailable,
    NoQuota,
    Unknown
}

public sealed class UsageWindowItemViewModel : INotifyPropertyChanged
{
    public UsageWindowItemViewModel(
        UsageWindow window,
        bool showBar,
        AppLanguage language,
        DepletionPrediction? prediction = null)
    {
        var usesUsedPercent = UsageWindowLabelText.IsUsedPercent(window);
        Label = usesUsedPercent
            ? UsageWindowLabelText.ShortFor(window, language)
            : UsageWindowLabelText.For(window, language);
        ShowBar = showBar;
        PercentValue = window.PercentRemaining;
        PercentText = usesUsedPercent
            ? AppText.Get(language, AppStringKeys.PercentUsedValue, window.PercentRemaining)
            : AppText.Get(language, AppStringKeys.PercentLeftValue, window.PercentRemaining);
        ResetText = usesUsedPercent && string.IsNullOrWhiteSpace(window.ResetLabel) && window.ResetAt is null
            ? string.Empty
            : !string.IsNullOrWhiteSpace(window.ResetLabel)
                ? LocalizeResetLabel(window.ResetLabel!, language)
                : window.ResetAt is null
                    ? AppText.Get(language, AppStringKeys.ResetUnavailable)
                    : AppText.Get(language, AppStringKeys.ResetAt, window.ResetAt.Value.LocalDateTime);
        ConfidenceText = AppText.Get(language, AppStringKeys.ConfidenceValue, window.Confidence);
        IsLowConfidence = window.Confidence == "medium";
        UrgencyBrush = ProviderUsageItemViewModel.ComputeUrgencyBrush(window.PercentRemaining, usesUsedPercent);
        ProviderUsageItemViewModel.ApplyPrediction(
            prediction,
            window,
            language,
            out var predictionText,
            out var predictionDetailText,
            out var sparklinePoints,
            out var sparklineProjectionPoints);
        ShowPrediction = prediction?.State is
            PredictionState.Collecting or PredictionState.WaitingForChange or PredictionState.Depleted
            or PredictionState.WillDeplete or PredictionState.ResetsFirst;
        PredictionText = predictionText;
        PredictionDetailText = predictionDetailText;
        SparklinePoints = sparklinePoints;
        SparklineProjectionPoints = sparklineProjectionPoints;
    }

    public string Label { get; }
    public bool ShowBar { get; }
    public int PercentValue { get; }
    public string PercentText { get; }
    public string ResetText { get; }
    public string ConfidenceText { get; }
    public bool IsLowConfidence { get; }
    public string UrgencyBrush { get; }
    public bool ShowPrediction { get; }
    public string PredictionText { get; }
    public string PredictionDetailText { get; }
    public string SparklinePoints { get; }
    public string SparklineProjectionPoints { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaiseColorProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UrgencyBrush)));
    }

    private static string LocalizeResetLabel(string label, AppLanguage language)
    {
        if (label == "Not active in session")
        {
            return AppText.Get(language, AppStringKeys.NotActiveInSession);
        }

        return label;
    }
}

internal static class UsageWindowLabelText
{
    public static string For(UsageWindow window, AppLanguage language)
    {
        return AppLanguageResolver.Resolve(language) switch
        {
            AppLanguage.Korean => window.Id switch
            {
                "five-hour" => "5시간 한도",
                "weekly" => "주간 한도",
                "monthly" => "월간 한도",
                "weekly-sonnet" => "Sonnet 주간 한도",
                "weekly-opus" => "Opus 주간 한도",
                "weekly-routines" => "Daily Routines 주간 한도",
                "weekly-cowork" => "Cowork 주간 한도",
                _ => window.Label
            },
            AppLanguage.Japanese => window.Id switch
            {
                "five-hour" => "5時間上限",
                "weekly" => "週次上限",
                "monthly" => "月間上限",
                "weekly-sonnet" => "Sonnet 週次上限",
                "weekly-opus" => "Opus 週次上限",
                "weekly-routines" => "Daily Routines 週次上限",
                "weekly-cowork" => "Cowork 週次上限",
                _ => window.Label
            },
            AppLanguage.Chinese => window.Id switch
            {
                "five-hour" => "5小时限制",
                "weekly" => "每周限制",
                "monthly" => "每月限制",
                "weekly-sonnet" => "Sonnet 每周限制",
                "weekly-opus" => "Opus 每周限制",
                "weekly-routines" => "Daily Routines 每周限制",
                "weekly-cowork" => "Cowork 每周限制",
                _ => window.Label
            },
            _ => window.Label
        };
    }

    public static bool IsUsedPercent(UsageWindow window)
    {
        return window.IsUsedPercent;
    }

    public static string ShortFor(UsageWindow window, AppLanguage language)
    {
        if (IsUsedPercent(window)
            && window.Id.StartsWith("antigravity-", StringComparison.Ordinal))
        {
            return CompactAntigravityLabel(window.Label);
        }

        return For(window, language);
    }

    private static string CompactAntigravityLabel(string label)
    {
        var normalized = label.ToLowerInvariant();
        var strength = AntigravityStrengthSuffix(label, compact: false);
        if (normalized.Contains("gemini"))
        {
            if (normalized.Contains("flash"))
            {
                return $"Gemini Flash{strength}";
            }

            if (normalized.Contains("pro"))
            {
                return $"Gemini Pro{strength}";
            }

            return $"Gemini{strength}";
        }

        if (normalized.Contains("claude"))
        {
            if (normalized.Contains("opus"))
            {
                return $"Claude Opus{strength}";
            }

            if (normalized.Contains("sonnet"))
            {
                return $"Claude Sonnet{strength}";
            }

            return $"Claude{strength}";
        }

        if (normalized.Contains("gpt") || normalized.Contains("oss"))
        {
            return $"GPT-OSS{strength}";
        }

        return Regex.Replace(label, @"\s*\((?:Low|Medium|High|Thinking)\)\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static string AntigravityStrengthSuffix(string label, bool compact)
    {
        var match = Regex.Match(label, @"\((Low|Medium|High|Thinking)\)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return string.Empty;
        }

        var value = match.Groups[1].Value;
        if (!compact)
        {
            return $" ({value})";
        }

        return value.ToLowerInvariant() switch
        {
            "low" => " L",
            "medium" => " M",
            "high" => " H",
            "thinking" => " T",
            _ => $" {value}"
        };
    }
}
