using System.Drawing;
using System.Media;
using System.Windows.Forms;
using System.Windows.Threading;
using AiLimit.App.Localization;
using AiLimit.App.Services;
using AiLimit.Core.Settings;
using Microsoft.Toolkit.Uwp.Notifications;

namespace AiLimit.App.Tray;

public sealed class TrayController : IDisposable
{
    private const string ProviderStatusTag = "provider-status";

    private readonly AppState _state;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _summaryItem;
    private readonly ToolStripMenuItem _providersHeaderItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _languageItem;
    private readonly ToolStripMenuItem _toggleWidgetItem;
    private readonly ToolStripMenuItem _openDashboardItem;
    private readonly ToolStripMenuItem _refreshNowItem;
    private readonly ToolStripMenuItem _copyDiagnosticLogItem;
    private readonly ToolStripMenuItem _quitItem;
    private readonly Dictionary<AppLanguage, ToolStripMenuItem> _languageItems;
    private readonly DispatcherTimer _singleClickTimer;
    private readonly DispatcherTimer _providerHoverTimer;
    private readonly HashSet<string> _warnedLimits = new(StringComparer.Ordinal);
    private readonly OnActivated _toastActivatedHandler;
    private string? _pendingExpandedProviderId;
    private string? _expandedProviderId;
    private bool _isUpdatingProviderItems;
    private uint _warningToastSequence;

    public TrayController(AppState state)
    {
        _state = state;
        _summaryItem = new ToolStripMenuItem("Refresh to load usage") { Enabled = false };
        _providersHeaderItem = new ToolStripMenuItem("Tracked models") { Enabled = false };
        _settingsItem = new ToolStripMenuItem("Settings");
        _languageItem = new ToolStripMenuItem("Language");
        _toggleWidgetItem = new ToolStripMenuItem("Show Widget", null, (_, _) => _state.ToggleWidget());
        _openDashboardItem = new ToolStripMenuItem("Open Dashboard", null, (_, _) => _state.ShowDashboard());
        _refreshNowItem = new ToolStripMenuItem("Refresh Now", null, async (_, _) => await _state.RefreshNowAsync());
        _copyDiagnosticLogItem = new ToolStripMenuItem("Copy Diagnostic Log", null, (_, _) => CopyDiagnosticLog());
        _quitItem = new ToolStripMenuItem("Quit", null, (_, _) => _state.Shutdown());
        _languageItems = AppLanguageCatalog.SupportedLanguages.ToDictionary(
            item => item.Language,
            item => new ToolStripMenuItem(item.EnglishName, null, (_, _) => _state.SetLanguage(item.Language)));
        _toastActivatedHandler = toastArgs => OnToastActivated(toastArgs.Argument);

        foreach (var item in _languageItems.Values)
        {
            _languageItem.DropDownItems.Add(item);
        }

        _settingsItem.DropDownItems.Add(_toggleWidgetItem);
        _settingsItem.DropDownItems.Add(new ToolStripSeparator());
        _settingsItem.DropDownItems.Add(_languageItem);
        _settingsItem.DropDownItems.Add(new ToolStripSeparator());
        _settingsItem.DropDownItems.Add(_copyDiagnosticLogItem);

        _singleClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(260)
        };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            _state.ToggleWidget();
        };
        _providerHoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _providerHoverTimer.Tick += (_, _) =>
        {
            _providerHoverTimer.Stop();
            if (_pendingExpandedProviderId == _expandedProviderId)
            {
                return;
            }

            _expandedProviderId = _pendingExpandedProviderId;
            UpdateSummaryItems(AppLanguageResolver.Resolve(_state.DisplayLanguage));
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_summaryItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_openDashboardItem);
        menu.Items.Add(_refreshNowItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_providersHeaderItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_quitItem);
        menu.Closed += (_, _) =>
        {
            _providerHoverTimer.Stop();
            _pendingExpandedProviderId = null;
            _expandedProviderId = null;
            UpdateSummaryItems(AppLanguageResolver.Resolve(_state.DisplayLanguage));
        };

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(),
            Text = "Quota Watch",
            Visible = true
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _singleClickTimer.Stop();
                _singleClickTimer.Start();
            }
        };
        _notifyIcon.DoubleClick += (_, _) =>
        {
            _singleClickTimer.Stop();
            _state.ShowDashboard();
        };

        _state.SnapshotChanged += OnStateChanged;
        _state.SettingsChanged += OnStateChanged;
        _state.WidgetVisibilityChanged += OnStateChanged;
        ToastNotificationManagerCompat.OnActivated += _toastActivatedHandler;
        UpdateText();
    }

    public void Dispose()
    {
        _singleClickTimer.Stop();
        _providerHoverTimer.Stop();
        ToastNotificationManagerCompat.OnActivated -= _toastActivatedHandler;
        _state.SnapshotChanged -= OnStateChanged;
        _state.SettingsChanged -= OnStateChanged;
        _state.WidgetVisibilityChanged -= OnStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateText();
        ShowLimitWarningIfNeeded();
    }

    private void UpdateText()
    {
        var selectedLanguage = _state.DisplayLanguage;
        var language = AppLanguageResolver.Resolve(selectedLanguage);

        _settingsItem.Text = AppText.Get(language, AppStringKeys.TraySettingsMenu);
        _toggleWidgetItem.Text = _state.IsWidgetVisible
            ? AppText.Get(language, AppStringKeys.TrayHideWidget)
            : AppText.Get(language, AppStringKeys.TrayShowWidget);
        _openDashboardItem.Text = AppText.Get(language, AppStringKeys.TrayOpenDashboard);
        _refreshNowItem.Text = AppText.Get(language, AppStringKeys.TrayRefreshNow);
        _copyDiagnosticLogItem.Text = AppText.Get(language, AppStringKeys.TrayCopyDiagnosticLog);
        _quitItem.Text = AppText.Get(language, AppStringKeys.TrayQuit);
        _providersHeaderItem.Text = AppText.Get(language, AppStringKeys.TrayTrackedModels);
        _languageItem.Text = AppText.Get(language, AppStringKeys.TrayLanguageMenu);
        UpdateLanguageItems(language, selectedLanguage);
        UpdateSummaryItems(language);

        _notifyIcon.Text = TruncateNotifyText($"Quota Watch - {TrayStatusText.BuildSummary(_state.CurrentSnapshots, language)}");
    }

    private void CopyDiagnosticLog()
    {
        var language = AppLanguageResolver.Resolve(_state.DisplayLanguage);
        try
        {
            Clipboard.SetText(AppLog.ReadDiagnosticLogForCopy());
            _notifyIcon.ShowBalloonTip(
                3000,
                AppText.Get(language, AppStringKeys.TrayLogCopied),
                AppText.Get(language, AppStringKeys.TrayLogCopiedDetail),
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, "Tray", $"Diagnostic log copy failed. {ex.GetType().Name}: {ex.Message}");
            _notifyIcon.ShowBalloonTip(
                3000,
                AppText.Get(language, AppStringKeys.TrayLogCopyFailed),
                ex.Message,
                ToolTipIcon.Warning);
        }
    }

    private void UpdateSummaryItems(AppLanguage language)
    {
        if (_isUpdatingProviderItems)
        {
            return;
        }

        var menu = _notifyIcon.ContextMenuStrip;
        if (menu is null)
        {
            return;
        }

        _isUpdatingProviderItems = true;
        try
        {
            _summaryItem.Text = TrayStatusText.BuildSummary(_state.CurrentSnapshots, language);

            var providerItems = TrayStatusText.BuildProviderItems(_state.CurrentSnapshots, language);
            if (_expandedProviderId is not null
                && providerItems.All(item => item.ProviderId != _expandedProviderId))
            {
                _expandedProviderId = null;
            }

            var insertAt = menu.Items.IndexOf(_providersHeaderItem) + 1;
            while (insertAt < menu.Items.Count
                && menu.Items[insertAt] is ToolStripMenuItem item
                && item.Tag as string == ProviderStatusTag)
            {
                menu.Items.RemoveAt(insertAt);
            }

            foreach (var line in TrayStatusText.BuildVisibleProviderLines(providerItems, _expandedProviderId))
            {
                var text = line.IsDetail
                    ? $"    {line.Text}"
                    : $"{line.Text}  {(line.ProviderId == _expandedProviderId ? "▼" : "▶")}";
                var menuItem = new ToolStripMenuItem(text)
                {
                    Enabled = !line.IsDetail,
                    Tag = ProviderStatusTag
                };
                if (!line.IsDetail)
                {
                    menuItem.MouseEnter += (_, _) => ScheduleProviderExpansion(line.ProviderId);
                }

                menu.Items.Insert(insertAt, menuItem);
                insertAt++;
            }
        }
        finally
        {
            _isUpdatingProviderItems = false;
        }
    }

    private void ScheduleProviderExpansion(string providerId)
    {
        _pendingExpandedProviderId = providerId;
        _providerHoverTimer.Stop();
        _providerHoverTimer.Start();
    }

    private void ShowLimitWarningIfNeeded()
    {
        if (!_state.CurrentSettings.IsLimitWarningEnabled)
        {
            return;
        }

        var limitWarningSettings = _state.CurrentSettings.Normalize().LimitWarningSettings;
        var evaluationSnapshots = _state.WarningEvaluationSnapshots;
        LimitWarningEvaluator.UpdateWarningState(
            evaluationSnapshots,
            _warnedLimits,
            limitWarningSettings);
        var warnings = LimitWarningEvaluator.FindWarnings(
            evaluationSnapshots,
            _warnedLimits,
            _state.CurrentSettings.WeeklyLimitWarningSuppressions,
            thresholdPercent: _state.CurrentSettings.LimitWarningThresholdPercent,
            providerThresholds: limitWarningSettings);
        if (warnings.Count == 0)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            _warnedLimits.Add(warning.Key);
        }

        SystemSounds.Exclamation.Play();
        var language = AppLanguageResolver.Resolve(_state.DisplayLanguage);
        foreach (var warning in warnings)
        {
            if (warning.IsWeeklyLimit)
            {
                ShowWeeklyLimitWarning(warning, language);
            }
            else
            {
                ShowStandardLimitWarning(warning, language);
            }
        }
    }

    private void ShowStandardLimitWarning(LimitWarning warning, AppLanguage language)
    {
        new ToastContentBuilder()
            .AddText(LimitWarningText.Title(language))
            .AddText(LimitWarningText.BuildBody(warning, language))
            .Show(toast =>
            {
                toast.Tag = NextWarningToastTag();
                toast.Group = "ai-limit";
            });
    }

    private void ShowWeeklyLimitWarning(LimitWarning warning, AppLanguage language)
    {
        new ToastContentBuilder()
            .AddArgument("action", "weekly-warning")
            .AddText(LimitWarningText.WeeklyTitle(language))
            .AddText(LimitWarningText.BuildBody(warning, language))
            .AddButton(new ToastButton()
                .SetContent(LimitWarningText.SuppressWeeklyButton(language))
                .AddArgument("action", "suppress-weekly-warnings")
                .AddArgument("providerId", warning.ProviderId)
                .AddArgument("windowId", warning.WindowId)
                .AddArgument("resetAt", warning.ResetAt?.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                .AddArgument("accountKey", warning.AccountKey ?? string.Empty)
                .SetBackgroundActivation())
            .AddButton(new ToastButtonDismiss(LimitWarningText.DismissButton(language)))
            .Show(toast =>
            {
                toast.Tag = NextWarningToastTag();
                toast.Group = "ai-limit";
            });
    }

    private string NextWarningToastTag()
    {
        unchecked
        {
            _warningToastSequence++;
        }

        return $"limit-{_warningToastSequence:X8}";
    }

    private void OnToastActivated(string argument)
    {
        var args = ToastArguments.Parse(argument);
        if (args.TryGetValue("action", out var action)
            && action == "suppress-weekly-warnings")
        {
            args.TryGetValue("providerId", out var providerId);
            args.TryGetValue("windowId", out var windowId);
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(windowId))
            {
                return;
            }

            DateTimeOffset? resetAt = null;
            if (args.TryGetValue("resetAt", out var resetAtValue)
                && long.TryParse(resetAtValue, out var resetAtSeconds))
            {
                resetAt = DateTimeOffset.FromUnixTimeSeconds(resetAtSeconds);
            }

            args.TryGetValue("accountKey", out var accountKey);
            accountKey = string.IsNullOrWhiteSpace(accountKey) ? null : accountKey;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _state.SuppressWeeklyLimitWarning(providerId, windowId, resetAt, accountKey);
            });
        }
    }

    private void UpdateLanguageItems(AppLanguage displayLanguage, AppLanguage selectedLanguage)
    {
        foreach (var catalogItem in AppLanguageCatalog.SupportedLanguages)
        {
            if (_languageItems.TryGetValue(catalogItem.Language, out var menuItem))
            {
                menuItem.Text = AppLanguageCatalog.LabelFor(catalogItem, displayLanguage);
                menuItem.Checked = catalogItem.Language == selectedLanguage;
            }
        }
    }

    private static string TruncateNotifyText(string text)
    {
        const int maxLength = 63;
        return text.Length <= maxLength
            ? text
            : $"{text[..(maxLength - 1)]}...";
    }

    private static Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/Icons/tray.ico"));

        return resource is null ? SystemIcons.Application : new Icon(resource.Stream);
    }
}
