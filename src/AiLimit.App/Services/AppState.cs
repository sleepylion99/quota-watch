using System.Windows.Threading;
using AiLimit.App.Windows;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Refresh;
using AiLimit.Core.Settings;
using AiLimit.Core.Storage;

namespace AiLimit.App.Services;

public sealed class AppState
{
    private readonly UsageRefreshService _refreshService = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ProviderAutoRefreshCoordinator _autoRefresh = new();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private bool _isRefreshing;
    private DashboardWindow? _dashboardWindow;
    private WidgetWindow? _widgetWindow;
    private IReadOnlyList<UsageSnapshot> _latestSnapshots = [];
    private AppLanguage? _previewLanguage;

    public ThemeService? ThemeService { get; set; }

    public AppState()
    {
        SettingsStore = new SettingsStore(AppPaths.SettingsFile);
        SnapshotStore = new SnapshotStore(AppPaths.SnapshotsFile);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _timer.Tick += async (_, _) => await RefreshNextProviderAsync();
    }

    public event EventHandler? SnapshotChanged;

    public event EventHandler? SettingsChanged;

    public event EventHandler? WidgetVisibilityChanged;

    public SettingsStore SettingsStore { get; }

    public SnapshotStore SnapshotStore { get; }

    public UsageSnapshot? CurrentSnapshot => CurrentSnapshots.FirstOrDefault();

    public IReadOnlyList<UsageSnapshot> CurrentSnapshots { get; private set; } = [];

    public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;

    public AppLanguage DisplayLanguage => _previewLanguage ?? CurrentSettings.Language;

    public bool IsRefreshing => _isRefreshing;

    public bool IsWidgetVisible => _widgetWindow?.IsVisible == true;

    public IReadOnlyDictionary<string, ProviderAutoRefreshStatus> AutoRefreshStatuses => _autoRefresh.Statuses;

    public Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        return _updateChecker.CheckAsync(_shutdown.Token);
    }

    public async Task InitializeAsync()
    {
        CurrentSettings = (await SettingsStore.LoadAsync(_shutdown.Token)).Normalize();
        await SettingsStore.SaveAsync(CurrentSettings, _shutdown.Token);
        _latestSnapshots = await SnapshotStore.LoadAllAsync(_shutdown.Token);
        CurrentSnapshots = FilterSnapshotsByEnabledProviders(_latestSnapshots, CurrentSettings);

        ConfigureTimer();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);

        _ = RefreshNowInBackgroundAsync();
    }

    private async Task RefreshNowInBackgroundAsync()
    {
        try
        {
            await RefreshNowAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write("AppState", $"Startup refresh failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task RefreshNowAsync()
    {
        if (_isRefreshing || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _isRefreshing = true;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var providers = GetActiveProviders(CurrentSettings);
            var refreshed = await Task.Run(
                () => _refreshService.RefreshAllAsync(providers, _shutdown.Token),
                _shutdown.Token);
            await ApplyRefreshedSnapshotsAsync(refreshed);
        }
        finally
        {
            _isRefreshing = false;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            ConfigureTimer();
        }
    }

    private async Task RefreshNextProviderAsync()
    {
        if (_isRefreshing || _shutdown.IsCancellationRequested)
        {
            return;
        }

        var providers = GetActiveProviders(CurrentSettings);
        if (providers.Count == 0)
        {
            return;
        }

        var provider = _autoRefresh.SelectNext(providers, DateTimeOffset.Now);
        if (provider is null)
        {
            ConfigureTimer();
            return;
        }

        _isRefreshing = true;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var refreshed = await Task.Run(
                () => _refreshService.RefreshAsync(provider, _shutdown.Token),
                _shutdown.Token);
            await ApplyRefreshedSnapshotsAsync([refreshed]);
        }
        finally
        {
            _isRefreshing = false;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            ConfigureTimer();
        }
    }

    public void ShowDashboard()
    {
        if (_dashboardWindow is null)
        {
            _dashboardWindow = new DashboardWindow(this);
            _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
        }

        _dashboardWindow.Topmost = CurrentSettings.IsDashboardAlwaysOnTop;
        _dashboardWindow.Show();
        _dashboardWindow.Activate();
    }

    public void ToggleWidget()
    {
        if (IsWidgetVisible)
        {
            _widgetWindow?.Hide();
            QueueSettingsUpdate("Save widget hidden setting", settings => settings with { IsWidgetVisible = false });
        }
        else
        {
            ShowWidget();
            QueueSettingsUpdate("Save widget visible setting", settings => settings with { IsWidgetVisible = true });
        }

        WidgetVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowWidget()
    {
        if (_widgetWindow is null)
        {
            _widgetWindow = new WidgetWindow(this);
            _widgetWindow.Closed += (_, _) => _widgetWindow = null;
        }

        _widgetWindow.Left = CurrentSettings.WidgetLeft;
        _widgetWindow.Top = CurrentSettings.WidgetTop;
        _widgetWindow.Topmost = CurrentSettings.IsWidgetAlwaysOnTop;
        _widgetWindow.Show();
        WidgetVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateWidgetPlacement(double left, double top)
    {
        QueueSettingsUpdate("Save widget placement", settings => settings with
        {
            WidgetLeft = left,
            WidgetTop = top
        });
    }

    public void SetLimitDisplayMode(LimitDisplayMode mode)
    {
        if (CurrentSettings.LimitDisplayMode == mode)
        {
            return;
        }

        QueueSettingsUpdate("Save limit display mode", settings => settings with { LimitDisplayMode = mode });
    }

    public void SetLanguage(AppLanguage language)
    {
        if (CurrentSettings.Language == language)
        {
            return;
        }

        QueueSettingsUpdate("Save language", settings => settings with { Language = language });
    }

    public void SetThemeMode(AppThemeMode mode)
    {
        if (CurrentSettings.ThemeMode == mode)
        {
            return;
        }

        ThemeService?.Apply(mode);
        QueueSettingsUpdate("Save theme mode", settings => settings with { ThemeMode = mode });
    }

    public void SetDashboardOpacity(double opacity)
    {
        var clamped = AppSettings.ClampOpacity(opacity);
        if (Math.Abs(CurrentSettings.DashboardOpacity - clamped) < 0.0005)
        {
            return;
        }

        QueueSettingsUpdate("Save dashboard opacity", settings => settings with { DashboardOpacity = clamped });
    }

    public void SetWidgetOpacity(double opacity)
    {
        var clamped = AppSettings.ClampOpacity(opacity);
        if (Math.Abs(CurrentSettings.WidgetOpacity - clamped) < 0.0005)
        {
            return;
        }

        QueueSettingsUpdate("Save widget opacity", settings => settings with { WidgetOpacity = clamped });
    }

    public void SetWidgetAlwaysOnTop(bool isAlwaysOnTop)
    {
        if (CurrentSettings.IsWidgetAlwaysOnTop == isAlwaysOnTop)
        {
            return;
        }

        QueueSettingsUpdate(
            "Save widget always-on-top setting",
            settings => settings with { IsWidgetAlwaysOnTop = isAlwaysOnTop });
    }

    public void SetDashboardAlwaysOnTop(bool isAlwaysOnTop)
    {
        if (CurrentSettings.IsDashboardAlwaysOnTop == isAlwaysOnTop)
        {
            return;
        }

        QueueSettingsUpdate(
            "Save dashboard always-on-top setting",
            settings => settings with { IsDashboardAlwaysOnTop = isAlwaysOnTop });
    }

    public void PreviewLanguage(AppLanguage language)
    {
        if (_previewLanguage == language)
        {
            return;
        }

        _previewLanguage = language;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearLanguagePreview()
    {
        if (_previewLanguage is null)
        {
            return;
        }

        _previewLanguage = null;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SuppressWeeklyLimitWarning(
        string providerId,
        string windowId,
        DateTimeOffset? resetAt,
        string? accountKey)
    {
        var suppression = new WeeklyLimitWarningSuppression(providerId, windowId, resetAt, accountKey);
        if ((CurrentSettings.WeeklyLimitWarningSuppressions ?? []).Contains(suppression))
        {
            return;
        }

        QueueSettingsUpdate("Save weekly warning preference", settings =>
        {
            var suppressions = settings.WeeklyLimitWarningSuppressions ?? [];
            return settings with
            {
                WeeklyLimitWarningSuppressions = suppressions.Append(suppression).Distinct().ToList()
            };
        });
    }

    public void SetProviderEnabled(string providerId, bool isEnabled)
    {
        var updatedSettings = UpdateProviderEnabled(CurrentSettings, providerId, isEnabled);

        CaptureLatestSnapshotsFromCurrentIfNeeded();
        CurrentSnapshots = FilterSnapshotsByEnabledProviders(_latestSnapshots, updatedSettings);
        QueueSettingsUpdate(
            "Save provider enabled setting",
            settings => UpdateProviderEnabled(settings, providerId, isEnabled),
            refreshAfterSave: true);
    }

    private static AppSettings UpdateProviderEnabled(AppSettings settings, string providerId, bool isEnabled)
    {
        var updatedProviders = settings.GetEffectiveProviders()
            .Select(provider => provider.Id == providerId ? provider with { IsEnabled = isEnabled } : provider)
            .ToList();
        return (settings with { Providers = updatedProviders }).Normalize();
    }

    public void SaveSettingsFromDashboard(AppSettings updatedSettings)
    {
        CaptureLatestSnapshotsFromCurrentIfNeeded();
        var normalizedSettings = updatedSettings.Normalize();
        CurrentSnapshots = FilterSnapshotsByEnabledProviders(_latestSnapshots, normalizedSettings);
        QueueSettingsUpdate(
            "Save dashboard settings",
            _ => normalizedSettings,
            refreshAfterSave: true);
    }

    private void CaptureLatestSnapshotsFromCurrentIfNeeded()
    {
        var currentById = CurrentSnapshots.ToDictionary(snapshot => snapshot.ProviderId, StringComparer.Ordinal);
        var latestById = _latestSnapshots.ToDictionary(snapshot => snapshot.ProviderId, StringComparer.Ordinal);
        foreach (var snapshot in currentById.Values)
        {
            latestById[snapshot.ProviderId] = snapshot;
        }

        _latestSnapshots = latestById.Values.ToList();
    }

    private async Task ApplyRefreshedSnapshotsAsync(IEnumerable<UsageSnapshot> refreshed)
    {
        var latestById = _latestSnapshots.ToDictionary(s => s.ProviderId, StringComparer.Ordinal);
        foreach (var snapshot in refreshed)
        {
            latestById[snapshot.ProviderId] = snapshot;
            _autoRefresh.RecordResult(snapshot);
        }

        _latestSnapshots = latestById.Values.ToList();
        CurrentSnapshots = FilterSnapshotsByEnabledProviders(_latestSnapshots, CurrentSettings);
        _autoRefresh.RemoveMissingProviders(CurrentSettings.GetEffectiveProviders()
            .Where(provider => provider.IsEnabled)
            .Select(provider => provider.Id));
        var prunedSettings = CurrentSettings.PruneExpiredWeeklyLimitWarningSuppressions(DateTimeOffset.Now);
        if (!Equals(prunedSettings, CurrentSettings))
        {
            CurrentSettings = prunedSettings;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            await SettingsStore.SaveAsync(CurrentSettings, _shutdown.Token);
        }

        if (_latestSnapshots.Count > 0)
        {
            await SnapshotStore.SaveAllAsync(_latestSnapshots, _shutdown.Token);
        }
    }

    public static IReadOnlyList<IUsageProvider> GetActiveProviders(AppSettings settings)
    {
        var effectiveProviders = settings.GetEffectiveProviders();
        var settingsById = effectiveProviders.ToDictionary(provider => provider.Id, StringComparer.Ordinal);
        var enabledById = effectiveProviders
            .Where(provider => provider.IsEnabled)
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);

        return ProviderCatalog.KnownProviders
            .Where(provider => enabledById.Contains(provider.Id))
            .Select(provider => CreateUsageProvider(provider, settingsById[provider.Id]))
            .ToList();
    }

    public static IReadOnlyList<UsageSnapshot> FilterSnapshotsByEnabledProviders(
        IReadOnlyList<UsageSnapshot> snapshots,
        AppSettings settings)
    {
        var enabledById = settings.GetEffectiveProviders()
            .Where(provider => provider.IsEnabled)
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);

        return snapshots
            .Where(snapshot => enabledById.Contains(snapshot.ProviderId))
            .ToList();
    }

    private static IUsageProvider CreateUsageProvider(ProviderCatalogItem provider, ProviderSetting setting)
    {
        if (provider.Id == "codex")
        {
            return new CodexUsageProvider(ToCodexUsageMode(setting.Mode));
        }

        if (provider.Id == "claude")
        {
            return new ClaudeUsageProvider(provider.Id, provider.DisplayName);
        }

        if (provider.Id == "gemini-pro")
        {
            return new AntigravityUsageProvider();
        }

        return new MockUsageProvider(
            provider.Id,
            provider.DisplayName,
            provider.FiveHourPercent,
            provider.WeeklyPercent);
    }

    private static CodexUsageMode ToCodexUsageMode(string? mode)
    {
        return ProviderSetting.NormalizeMode("codex", mode) switch
        {
            "basic" => CodexUsageMode.Basic,
            "pro" => CodexUsageMode.ProDetailed,
            _ => CodexUsageMode.Auto
        };
    }

    public void Shutdown()
    {
        _shutdown.Cancel();
        _timer.Stop();
        _widgetWindow?.Close();
        _dashboardWindow?.Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void ConfigureTimer()
    {
        _timer.Stop();
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        var interval = RefreshScheduler.ToInterval(CurrentSettings.RefreshCadence);
        if (interval is not null)
        {
            _timer.Interval = interval.Value;
            _timer.Start();
        }
    }

    private async Task SaveSettingsAsync(AppSettings settings)
    {
        ApplySettingsInMemory(settings);
        await SettingsStore.SaveAsync(settings, _shutdown.Token);
        ConfigureTimer();
    }

    private void QueueSettingsUpdate(
        string operation,
        Func<AppSettings, AppSettings> update,
        bool refreshAfterSave = false)
    {
        ApplySettingsInMemory(update(CurrentSettings).Normalize());
        RunBackgroundTask(operation, async () =>
        {
            await _settingsSaveGate.WaitAsync(_shutdown.Token);
            try
            {
                var latestSettings = update(CurrentSettings).Normalize();
                ApplySettingsInMemory(latestSettings);
                await SettingsStore.SaveAsync(latestSettings, _shutdown.Token);
                ConfigureTimer();
            }
            finally
            {
                _settingsSaveGate.Release();
            }

            if (refreshAfterSave)
            {
                await RefreshNowAsync();
            }
        });
    }

    private void ApplySettingsInMemory(AppSettings settings)
    {
        CurrentSettings = settings;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void RunBackgroundTask(string operation, Func<Task> action)
    {
        _ = RunAsync();

        async Task RunAsync()
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Write(AppLogLevel.Warning, "AppState", $"{operation} failed. {ex.GetType().Name}: {ex.Message}");
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }
    }
}
