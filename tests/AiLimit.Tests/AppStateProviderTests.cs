using AiLimit.App.Services;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class AppStateProviderTests
{
    [Fact]
    public void GetActiveProvidersReturnsOnlyEnabledProviders()
    {
        var settings = AppSettings.Default with
        {
            Providers = AppSettings.Default.GetEffectiveProviders()
                .Select(provider => provider.Id == "claude" ? provider with { IsEnabled = false } : provider)
                .ToList()
        };

        var active = AppState.GetActiveProviders(settings);

        Assert.DoesNotContain(active, provider => provider.Descriptor.Id == "claude");
        Assert.Contains(active, provider => provider.Descriptor.Id == "codex");
        Assert.IsType<CodexUsageProvider>(active.Single(provider => provider.Descriptor.Id == "codex"));
        Assert.IsType<AntigravityUsageProvider>(active.Single(provider => provider.Descriptor.Id == "gemini-pro"));
        Assert.IsType<AntigravityUsageProvider>(active.Single(provider => provider.Descriptor.Id == "gemini-pro"));
    }

    [Fact]
    public void GetActiveProvidersCreatesProDetailedCodexProviderFromSettings()
    {
        var settings = AppSettings.Default with
        {
            Providers = AppSettings.Default.GetEffectiveProviders()
                .Select(provider => provider.Id == "codex" ? provider with { Mode = "pro" } : provider)
                .ToList()
        };

        var active = AppState.GetActiveProviders(settings);

        var codex = Assert.IsType<CodexUsageProvider>(active.Single(provider => provider.Descriptor.Id == "codex"));
        var mode = typeof(CodexUsageProvider)
            .GetField("_mode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(codex);
        Assert.Equal(CodexUsageMode.ProDetailed, mode);
    }

    [Fact]
    public void SetProviderEnabledUpdatesCurrentSettings()
    {
        var state = new AppState();

        state.SetProviderEnabled("codex", false);

        Assert.False(state.CurrentSettings.GetEffectiveProviders().Single(provider => provider.Id == "codex").IsEnabled);
    }

    [Fact]
    public void LanguagePreviewChangesDisplayLanguageWithoutChangingSavedSettings()
    {
        var state = new AppState();

        state.PreviewLanguage(AppLanguage.Korean);

        Assert.Equal(AppLanguage.Korean, state.DisplayLanguage);
        Assert.Equal(AppLanguage.System, state.CurrentSettings.Language);

        state.ClearLanguagePreview();

        Assert.Equal(AppLanguage.System, state.DisplayLanguage);
        Assert.Equal(AppLanguage.System, state.CurrentSettings.Language);
    }

    [Fact]
    public void SetThemeModeUpdatesCurrentSettings()
    {
        var state = new AppState();

        state.SetThemeMode(AppThemeMode.Light);

        Assert.Equal(AppThemeMode.Light, state.CurrentSettings.ThemeMode);
    }

    [Fact]
    public void FilterSnapshotsByEnabledProvidersRemovesDisabledProvidersImmediately()
    {
        var snapshots = new[]
        {
            Snapshot("codex"),
            Snapshot("claude")
        };
        var settings = AppSettings.Default with
        {
            Providers = AppSettings.Default.GetEffectiveProviders()
                .Select(provider => provider.Id == "claude" ? provider with { IsEnabled = false } : provider)
                .ToList()
        };

        var filtered = AppState.FilterSnapshotsByEnabledProviders(snapshots, settings);

        Assert.Collection(
            filtered,
            snapshot => Assert.Equal("codex", snapshot.ProviderId));
    }

    [Fact]
    public void SetProviderEnabledRestoresCardWhenProviderIsReenabled()
    {
        var state = new AppState();
        var snapshots = new[]
        {
            Snapshot("codex"),
            Snapshot("claude")
        };
        typeof(AppState)
            .GetProperty(nameof(AppState.CurrentSnapshots))!
            .SetValue(state, snapshots);

        state.SetProviderEnabled("claude", false);
        state.SetProviderEnabled("claude", true);

        Assert.Contains(state.CurrentSnapshots, snapshot => snapshot.ProviderId == "claude");
    }

    [Fact]
    public void InitializeDoesNotBlockStartupOnProviderRefresh()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.App", "Services", "AppState.cs"));
        var start = source.IndexOf("public async Task InitializeAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task RefreshNowInBackgroundAsync()", StringComparison.Ordinal);
        var initializeBody = source[start..end];

        Assert.Contains("_ = RefreshNowInBackgroundAsync();", initializeBody);
        Assert.DoesNotContain("await RefreshNowAsync();", initializeBody);
        Assert.Contains("Task.Run(", source);
        Assert.Contains("_refreshService.RefreshAllAsync(providers, _shutdown.Token)", source);
        Assert.Contains("catch (Exception ex) when (ex is not OperationCanceledException)", source);
    }

    [Fact]
    public void TimerRefreshUsesAutomaticProviderSelection()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.App", "Services", "AppState.cs"));

        Assert.DoesNotContain("_roundRobinProviderIndex", source);
        Assert.Contains("ProviderAutoRefreshCoordinator", source);
        Assert.Contains("_timer.Tick += async (_, _) => await RefreshNextProviderAsync();", source);
        Assert.Contains("_autoRefresh.SelectNext(providers, DateTimeOffset.Now)", source);
        Assert.Contains("_refreshService.RefreshAsync(provider, _shutdown.Token)", source);
    }

    [Fact]
    public void AutoRefreshCoordinatorSelectsOldestEligibleProviderAndSkipsBackoff()
    {
        var now = DateTimeOffset.Parse("2026-06-01T12:00:00+09:00");
        var coordinator = new ProviderAutoRefreshCoordinator();
        var providers = new IUsageProvider[]
        {
            new StubProvider("codex"),
            new StubProvider("claude"),
            new StubProvider("gemini-pro")
        };
        coordinator.RecordResult(FailedSnapshot("codex", now.AddMinutes(-1)));
        coordinator.RecordResult(FailedSnapshot("codex", now));
        coordinator.RecordResult(FreshSnapshot("claude", now.AddMinutes(-5)));
        coordinator.RecordResult(FreshSnapshot("gemini-pro", now.AddMinutes(-20)));

        var selected = coordinator.SelectNext(providers, now);

        Assert.Equal("gemini-pro", selected?.Descriptor.Id);
    }

    [Fact]
    public void AutoRefreshCoordinatorRetriesFirstFailureOnNextTickAndBacksOffRepeatedFailures()
    {
        var now = DateTimeOffset.Parse("2026-06-01T12:00:00+09:00");
        var coordinator = new ProviderAutoRefreshCoordinator();

        coordinator.RecordResult(FailedSnapshot("codex", now));
        Assert.Equal(now, coordinator.Statuses["codex"].NextAutomaticRefreshAt);

        coordinator.RecordResult(FailedSnapshot("codex", now.AddMinutes(1)));
        Assert.Equal(now.AddMinutes(11), coordinator.Statuses["codex"].NextAutomaticRefreshAt);

        coordinator.RecordResult(FailedSnapshot("codex", now.AddMinutes(2)));
        Assert.Equal(now.AddMinutes(32), coordinator.Statuses["codex"].NextAutomaticRefreshAt);

        coordinator.RecordResult(FreshSnapshot("codex", now.AddMinutes(3)));
        Assert.Equal(0, coordinator.Statuses["codex"].FailureCount);
        Assert.Null(coordinator.Statuses["codex"].NextAutomaticRefreshAt);
    }

    [Fact]
    public void FireAndForgetSettingsWorkIsLoggedOnFailure()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.App", "Services", "AppState.cs"));

        Assert.Contains("RunBackgroundTask", source);
        Assert.DoesNotContain("_ = SaveSettingsAsync", source);
        Assert.DoesNotContain("_ = RefreshNowAsync", source);
        Assert.Contains("AppLog.Write(AppLogLevel.Warning, \"AppState\"", source);
    }

    [Fact]
    public void SettingsUpdatesAreSerializedAndRefreshRunsAfterPersist()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.App", "Services", "AppState.cs"));

        Assert.Contains("SemaphoreSlim _settingsSaveGate", source);
        Assert.Contains("QueueSettingsUpdate", source);
        Assert.Contains("await _settingsSaveGate.WaitAsync(_shutdown.Token)", source);
        Assert.Contains("if (refreshAfterSave)", source);
        Assert.Contains("await RefreshNowAsync();", source);
        Assert.DoesNotContain("RunBackgroundTask(\"Refresh after provider enabled change\", RefreshNowAsync)", source);
        Assert.DoesNotContain("RunBackgroundTask(\"Refresh after dashboard settings change\", RefreshNowAsync)", source);
    }

    [Fact]
    public void ConfigureTimerDoesNotRestartAfterShutdown()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.App", "Services", "AppState.cs"));
        var start = source.IndexOf("private void ConfigureTimer()", StringComparison.Ordinal);
        var configureTimerBody = source[start..];

        Assert.Contains("if (_shutdown.IsCancellationRequested)", configureTimerBody);
    }

    private static UsageSnapshot Snapshot(string providerId)
    {
        return new UsageSnapshot(
            providerId,
            providerId,
            DateTimeOffset.Parse("2026-05-18T02:00:00+09:00"),
            UsageSource.Mock,
            UsageStatus.Fresh,
            []);
    }

    private static UsageSnapshot FreshSnapshot(string providerId, DateTimeOffset checkedAt)
    {
        return new UsageSnapshot(providerId, providerId, checkedAt, UsageSource.Agent, UsageStatus.Fresh, []);
    }

    private static UsageSnapshot FailedSnapshot(string providerId, DateTimeOffset checkedAt)
    {
        return new UsageSnapshot(providerId, providerId, checkedAt, UsageSource.Agent, UsageStatus.Failed, [], "failed");
    }

    private static string SourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "quota-watch.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Repository root was not found.");
        }

        return Path.Combine([directory.FullName, .. segments]);
    }

    private sealed class StubProvider(string id) : IUsageProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(id, id, true);

        public Task<UsageSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(FreshSnapshot(id, DateTimeOffset.Now));
        }
    }
}
