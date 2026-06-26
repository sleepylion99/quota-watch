using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using AiLimit.App.ViewModels.Accounts;
using AiLimit.App.Windows;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Refresh;
using AiLimit.Core.Settings;
using AiLimit.Core.Storage;

namespace AiLimit.App.Services;

public sealed class AppState
{
    private readonly UsageRefreshService _refreshService = new();
    private readonly UpdateChecker _updateChecker = new();
    private static readonly TimeSpan InactiveAccountPollInterval = TimeSpan.FromMinutes(30);
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _inactiveTimer;
    private bool _isPollingInactive;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ProviderAutoRefreshCoordinator _autoRefresh = new();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private bool _isRefreshing;
    private DashboardWindow? _dashboardWindow;
    private WidgetWindow? _widgetWindow;
    private IReadOnlyList<UsageSnapshot> _latestSnapshots = [];
    private IReadOnlyList<UsageSample> _usageHistory = [];
    private IReadOnlyList<UsageSnapshot> _inactiveAccountSnapshots = [];
    private AppLanguage? _previewLanguage;

    public ThemeService? ThemeService { get; set; }

    public AppState()
    {
        SettingsStore = new SettingsStore(AppPaths.SettingsFile);
        SnapshotStore = new SnapshotStore(AppPaths.SnapshotsFile);
        UsageHistoryStore = new UsageHistoryStore(AppPaths.UsageHistoryFile);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _timer.Tick += async (_, _) => await RefreshNextProviderAsync();

        // Unlike _timer (started by ConfigureTimer after settings load), this fixed 30-minute timer
        // can start immediately: the first tick is far enough out that settings are loaded by then,
        // and RefreshInactiveAccountsAsync no-ops while the toggle is off (the default).
        _inactiveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = InactiveAccountPollInterval
        };
        _inactiveTimer.Tick += async (_, _) => await RefreshInactiveAccountsAsync();
        _inactiveTimer.Start();
    }

    public event EventHandler? SnapshotChanged;

    public event EventHandler? SettingsChanged;

    public event EventHandler? WidgetVisibilityChanged;

    public SettingsStore SettingsStore { get; }

    public SnapshotStore SnapshotStore { get; }

    public UsageHistoryStore UsageHistoryStore { get; }

    public UsageSnapshot? CurrentSnapshot => CurrentSnapshots.FirstOrDefault();

    public IReadOnlyList<UsageSnapshot> CurrentSnapshots { get; private set; } = [];

    public IReadOnlyList<UsageSnapshot> InactiveAccountSnapshots => _inactiveAccountSnapshots;

    /// <summary>Active-account snapshots plus polled inactive-account snapshots — the full set the limit-warning evaluator should consider.</summary>
    public IReadOnlyList<UsageSnapshot> WarningEvaluationSnapshots =>
        CurrentSnapshots.Concat(_inactiveAccountSnapshots).ToList();

    public IReadOnlyDictionary<UsageWindowKey, DepletionPrediction> CurrentPredictions { get; private set; } =
        new Dictionary<UsageWindowKey, DepletionPrediction>();

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
        _usageHistory = await UsageHistoryStore.LoadAsync(_shutdown.Token);
        CurrentSnapshots = FilterSnapshotsByEnabledProviders(_latestSnapshots, CurrentSettings);
        CurrentPredictions = BuildPredictions(CurrentSnapshots, _usageHistory, DateTimeOffset.UtcNow);

        ConfigureTimer();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);

        _ = RefreshNowInBackgroundAsync();
    }

    public async Task RefreshInactiveAccountsAsync()
    {
        if (_isPollingInactive
            || _shutdown.IsCancellationRequested
            || !CurrentSettings.IsInactiveAccountWarningEnabled)
        {
            return;
        }

        _isPollingInactive = true;
        try
        {
            var providers = new List<IUsageAccountProvider>
            {
                GetOrCreateClaudeAccountProvider(),
                GetOrCreateCodexAccountProvider(),
                GetOrCreateAntigravityAccountProvider()
            };
            var collector = new InactiveAccountUsageCollector(
                providers,
                log: message => AppLog.Write("InactiveAccounts", message));
            var snapshots = await Task.Run(
                () => collector.CollectAsync(_shutdown.Token),
                _shutdown.Token);

            _inactiveAccountSnapshots = FilterSnapshotsByEnabledProviders(snapshots, CurrentSettings);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Write("InactiveAccounts", $"Inactive refresh failed. {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _isPollingInactive = false;
        }
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
        if (normalizedSettings.IsInactiveAccountWarningEnabled)
        {
            _ = RefreshInactiveAccountsAsync();
        }
        else if (_inactiveAccountSnapshots.Count > 0)
        {
            // Toggle turned off: drop any inactive-account snapshots so stale data can't keep
            // contributing to warning evaluation until the next restart.
            _inactiveAccountSnapshots = [];
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
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
        var refreshedList = refreshed.ToList();
        var latestById = _latestSnapshots.ToDictionary(s => s.ProviderId, StringComparer.Ordinal);
        foreach (var snapshot in refreshedList)
        {
            latestById[snapshot.ProviderId] = snapshot;
            _autoRefresh.RecordResult(snapshot);
        }

        _latestSnapshots = latestById.Values.ToList();
        CurrentSnapshots = FilterSnapshotsByEnabledProviders(_latestSnapshots, CurrentSettings);
        var newSamples = CreateUsageSamples(refreshedList);
        if (newSamples.Count > 0)
        {
            _usageHistory = await UsageHistoryStore.AppendAsync(newSamples, _shutdown.Token);
        }

        CurrentPredictions = BuildPredictions(CurrentSnapshots, _usageHistory, DateTimeOffset.UtcNow);
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

    public static IReadOnlyList<UsageSample> CreateUsageSamples(IEnumerable<UsageSnapshot> snapshots)
    {
        return snapshots
            .Where(snapshot => snapshot.Status == UsageStatus.Fresh)
            .SelectMany(snapshot => snapshot.Windows.Select(window => new UsageSample(
                snapshot.ProviderId,
                window.Id,
                snapshot.CheckedAt.ToUniversalTime(),
                window.ConsumedPercent(),
                snapshot.AccountKey)))
            .ToList();
    }

    public static IReadOnlyDictionary<UsageWindowKey, DepletionPrediction> BuildPredictions(
        IReadOnlyList<UsageSnapshot> snapshots,
        IReadOnlyList<UsageSample> history,
        DateTimeOffset now)
    {
        var predictions = new Dictionary<UsageWindowKey, DepletionPrediction>();
        foreach (var snapshot in snapshots.Where(snapshot => snapshot.Status == UsageStatus.Fresh))
        {
            foreach (var window in snapshot.Windows)
            {
                var key = new UsageWindowKey(snapshot.ProviderId, window.Id);
                var samples = history
                    .Where(sample => sample.ProviderId == key.ProviderId
                        && sample.WindowId == key.WindowId
                        && sample.AccountKey == snapshot.AccountKey)
                    .ToList();
                var prediction = DepletionPredictor.Predict(
                    samples,
                    window.ConsumedPercent(),
                    window.ResetAt,
                    now);
                if (prediction.State != PredictionState.None)
                {
                    predictions[key] = prediction;
                }
            }
        }

        return predictions;
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
            .Select(provider => CreateUsageProvider(provider, settingsById[provider.Id], settings))
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

    // Shared services for Antigravity account-aware composition. Held statically
    // because CreateUsageProvider is static; the same instance must be reachable
    // from the future Accounts window (Task 11) so the dashboard tile and the
    // window agree on which account is active.
    private static AntigravityAccountProvider? _antigravityAccounts;
    private static readonly AccountTrashStore SharedAccountTrashStore =
        new(AccountTrashStore.DefaultPath());
    private static readonly object _antigravityAccountsLock = new();
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly HttpClient SharedLoginHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static AntigravityOAuthClientRegistry? _antigravityClientRegistry;
    private static readonly object _antigravityClientRegistryLock = new();

    internal static AntigravityOAuthClientRegistry GetOrCreateAntigravityClientRegistry()
    {
        if (_antigravityClientRegistry is { } existing)
        {
            return existing;
        }

        lock (_antigravityClientRegistryLock)
        {
            if (_antigravityClientRegistry is { } latest)
            {
                return latest;
            }

            _antigravityClientRegistry = new AntigravityOAuthClientRegistry(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AiLimit", "antigravity-oauth-clients.json"),
                AntigravityOAuthClientStore.DefaultPath(),
                new AntigravityAppBundleClientStore().Load() ?? AntigravityBundledOAuthClient.Config,
                new DpapiSecretProtector(),
                legacyLoader: () => OperatingSystem.IsWindows()
                    ? new AntigravityOAuthClientStore(AntigravityOAuthClientStore.DefaultPath()).LoadLegacyPlaintext()
                    : null);

            return _antigravityClientRegistry;
        }
    }

    internal static Func<CancellationToken, Task<AntigravityLoginResult>> BuildAntigravitySignIn()
    {
        var registry = GetOrCreateAntigravityClientRegistry();
        var accounts = GetOrCreateAntigravityAccountProvider();
        return ct => new AntigravityLoginFlow(
            activeClient: () =>
            {
                var a = registry.GetActive();
                return new AntigravityOAuthClientConfig(a?.ClientId, a?.ClientSecret);
            },
            listenerFactory: () => new LoopbackOAuthListener(),
            openBrowser: url => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }),
            httpClient: SharedLoginHttpClient,
            fetchEmail: (token, c) => new AntigravityUserInfoClient(SharedLoginHttpClient).FetchEmailAsync(token, c),
            addAccount: (email, refresh) =>
            {
                try
                {
                    accounts.Add(email, email, refresh);
                    return false; // newly added
                }
                catch (InvalidOperationException)
                {
                    return true; // duplicate refresh token — already added
                }
            }).SignInAsync(ct);
    }

    internal static ClaudeSignInViewModel BuildClaudeSignIn()
    {
        var flow = new ClaudeLoginFlow(
            openBrowser: url => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }),
            credentials: new ClaudeOAuthCredentialStore(SharedLoginHttpClient),
            writeProfile: cred => new ClaudeProfileWriter().WriteNewProfile(cred));
        return new ClaudeSignInViewModel(flow);
    }

    internal static AntigravityAccountProvider GetOrCreateAntigravityAccountProvider()
    {
        if (_antigravityAccounts is { } existing)
        {
            return existing;
        }

        lock (_antigravityAccountsLock)
        {
            if (_antigravityAccounts is { } latest)
            {
                return latest;
            }

            var store = new AntigravityAccountStore(AntigravityAccountStore.DefaultPath());
            var activeSelection = new AntigravityActiveSelection(AntigravityActiveSelection.DefaultPath());
            var userInfo = new AntigravityUserInfoClient(SharedHttpClient);

            _antigravityAccounts = new AntigravityAccountProvider(
                store: store,
                activeSelection: activeSelection,
                userInfo: userInfo,
                keychainRawBlobReader: AntigravityWindowsCredentialStore.ReadRawBlob,
                clientResolver: () =>
                {
                    var creds = new AntigravityOAuthCredentials(null, null, null, null, null);
                    var id = AntigravityOAuthCredentialStore.ResolveOAuthClientId(creds);
                    var secret = AntigravityOAuthCredentialStore.TryResolveOAuthClientSecret(creds);
                    return new AntigravityOAuthClientConfig(id, secret);
                },
                httpClient: SharedHttpClient,
                trashStore: SharedAccountTrashStore);

            return _antigravityAccounts;
        }
    }

    private static CodexAccountProvider? _codexAccounts;

    internal static CodexAccountProvider GetOrCreateCodexAccountProvider()
    {
        if (_codexAccounts is not null)
        {
            return _codexAccounts;
        }

        var scanner = new CodexProfileScanner();
        var selection = new CodexActiveSelection(CodexActiveSelection.DefaultPath());
        var binDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiLimit", "bin");
        var linker = new CodexProfileLinker(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            isElevated: IsProcessElevated,
            symlinks: new WindowsSymlinkCreator(),
            binDir: binDir);

        _codexAccounts = new CodexAccountProvider(
            scanner,
            selection,
            linker,
            poll: async (authPath, ct) =>
            {
                var snapshot = await new CodexUsageProvider(CodexUsageMode.Auto, authPath)
                    .RefreshAsync(ct).ConfigureAwait(false);
                return ToAccountSnapshot(snapshot);
            },
            trashStore: SharedAccountTrashStore,
            pollUsage: (authPath, ct) => new CodexUsageProvider(CodexUsageMode.Auto, authPath).RefreshAsync(ct));
        return _codexAccounts;
    }

    private static ClaudeAccountProvider? _claudeAccounts;

    internal static ClaudeAccountProvider GetOrCreateClaudeAccountProvider()
    {
        if (_claudeAccounts is not null)
        {
            return _claudeAccounts;
        }

        var scanner = new ClaudeProfileScanner();
        var selection = new ClaudeActiveSelection(ClaudeActiveSelection.DefaultPath());
        var profileUsagePoller = new ClaudeProfileUsagePoller(SharedLoginHttpClient);

        _claudeAccounts = new ClaudeAccountProvider(
            scanner,
            selection,
            poll: profileUsagePoller.PollAsync,
            trashStore: SharedAccountTrashStore,
            pollUsage: profileUsagePoller.PollUsageAsync);
        return _claudeAccounts;
    }

    private static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static AccountSnapshot ToAccountSnapshot(UsageSnapshot snapshot)
    {
        if (snapshot.Status == UsageStatus.Failed)
        {
            return AccountSnapshot.Failure(snapshot.LastError ?? "Codex poll failed.");
        }

        var buckets = snapshot.Windows
            .Select(w => new QuotaBucket(w.Label, w.PercentRemaining, w.ResetAt))
            .ToList()
            .AsReadOnly();
        return AccountSnapshot.Success(buckets, AccountPlan.Unknown);
    }

    private static IUsageProvider CreateUsageProvider(
        ProviderCatalogItem provider,
        ProviderSetting setting,
        AppSettings appSettings)
    {
        if (provider.Id == "codex")
        {
            var codexAccounts = GetOrCreateCodexAccountProvider();
            var authPath = codexAccounts.ResolveActiveAuthPath();
            return new CodexUsageProvider(ToCodexUsageMode(setting.Mode), authPath!);
        }

        if (provider.Id == "claude")
        {
            return new ClaudeUsageProvider(provider.Id, provider.DisplayName);
        }

        if (provider.Id == "gemini-pro")
        {
            var accounts = GetOrCreateAntigravityAccountProvider();
            return AntigravityUsageProvider.CreateWithCredentialResolver(
                credentialResolver: () => accounts.ResolveActiveCredentials(),
                httpClient: SharedHttpClient,
                allowLocalDetectionResolver: () => accounts.GetActiveId() is null);
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
        _inactiveTimer.Stop();
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
