using AiLimit.Core.Refresh;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task LoadAsyncReturnsDefaultsWhenFileIsMissing()
    {
        var directory = CreateTempDirectory();
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(RefreshCadence.FiveMinutes, settings.RefreshCadence);
        Assert.True(settings.IsWidgetVisible);
        Assert.False(settings.IsWidgetAlwaysOnTop);
        Assert.Equal(AppLanguage.System, settings.Language);
        Assert.Equal(AppThemeMode.System, settings.ThemeMode);
        Assert.Empty(settings.WeeklyLimitWarningSuppressions ?? []);
    }

    [Fact]
    public void DefaultThemeModeIsSystemForNewUsers()
    {
        Assert.Equal(AppThemeMode.System, AppSettings.Default.ThemeMode);
    }

    [Fact]
    public async Task LoadAsyncMigratesLegacyFileWithoutThemeModeToDark()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "settings.json");
        var legacyJson = """
{
  "RefreshCadence": "FiveMinutes",
  "IsWidgetVisible": true,
  "IsWidgetAlwaysOnTop": false,
  "WidgetLeft": 80,
  "WidgetTop": 80,
  "LimitDisplayMode": "BothText",
  "Language": "System"
}
""";
        await File.WriteAllTextAsync(path, legacyJson);
        var store = new SettingsStore(path);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(AppThemeMode.Dark, settings.ThemeMode);
    }

    [Fact]
    public async Task LoadAsyncRoundTripsExplicitThemeMode()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        await store.SaveAsync(
            AppSettings.Default with { ThemeMode = AppThemeMode.Light },
            CancellationToken.None);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(AppThemeMode.Light, settings.ThemeMode);
    }

    [Fact]
    public void NormalizeReplacesInvalidThemeModeWithSystem()
    {
        var settings = AppSettings.Default with { ThemeMode = (AppThemeMode)999 };

        Assert.Equal(AppThemeMode.System, settings.Normalize().ThemeMode);
    }

    [Fact]
    public async Task SaveAsyncPersistsSettings()
    {
        var directory = CreateTempDirectory();
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var expected = new AppSettings(
            RefreshCadence.OneMinute,
            IsWidgetVisible: false,
            IsWidgetAlwaysOnTop: false,
            WidgetLeft: 320,
            WidgetTop: 240);

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(expected.RefreshCadence, actual.RefreshCadence);
        Assert.Equal(expected.IsWidgetVisible, actual.IsWidgetVisible);
        Assert.Equal(expected.IsWidgetAlwaysOnTop, actual.IsWidgetAlwaysOnTop);
        Assert.Equal(expected.WidgetLeft, actual.WidgetLeft);
        Assert.Equal(expected.WidgetTop, actual.WidgetTop);
        Assert.Equal(expected.LimitDisplayMode, actual.LimitDisplayMode);
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.Normalize().WeeklyLimitWarningSuppressions, actual.WeeklyLimitWarningSuppressions);
        Assert.Equal(expected.Normalize().Providers, actual.Providers);
    }

    [Fact]
    public async Task SaveAsyncRoundTripsCodexProfilesAndSelection()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "settings.json");
        var authPath = Path.Combine(directory, "work", "auth.json");
        var store = new SettingsStore(path);
        var expected = AppSettings.Default with
        {
            CodexProfiles =
            [
                new CodexProfileSetting("work", "Work", authPath)
            ],
            SelectedCodexProfileId = "work"
        };

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("work", actual.SelectedCodexProfileId);
        var selected = actual.GetSelectedCodexProfile();
        Assert.Equal("Work", selected.DisplayName);
        Assert.Equal(Path.GetFullPath(authPath), selected.AuthPath);
    }

    [Fact]
    public void PruneExpiredWeeklyLimitWarningSuppressionsKeepsCurrentAndAccountScopedItems()
    {
        var settings = AppSettings.Default with
        {
            WeeklyLimitWarningSuppressions =
            [
                new WeeklyLimitWarningSuppression("codex", "weekly", DateTimeOffset.Parse("2026-05-31T00:00:00+09:00"), "email:a@example.com"),
                new WeeklyLimitWarningSuppression("gemini-pro", "antigravity-gemini-3-5-flash-medium", DateTimeOffset.Parse("2026-06-07T00:00:00+09:00"), "email:b@example.com")
            ]
        };

        var pruned = settings.PruneExpiredWeeklyLimitWarningSuppressions(DateTimeOffset.Parse("2026-06-01T00:00:00+09:00"));

        var suppression = Assert.Single(pruned.WeeklyLimitWarningSuppressions ?? []);
        Assert.Equal("gemini-pro", suppression.ProviderId);
        Assert.Equal("email:b@example.com", suppression.AccountKey);
    }

    [Fact]
    public void NormalizeClampsLimitWarningThreshold()
    {
        var tooLow = AppSettings.Default with { LimitWarningThresholdPercent = 0 };
        var tooHigh = AppSettings.Default with { LimitWarningThresholdPercent = 100 };
        var inRange = AppSettings.Default with { LimitWarningThresholdPercent = 50 };

        Assert.Equal(1, tooLow.Normalize().LimitWarningThresholdPercent);
        Assert.Equal(99, tooHigh.Normalize().LimitWarningThresholdPercent);
        Assert.Equal(50, inRange.Normalize().LimitWarningThresholdPercent);
    }

    [Fact]
    public void NormalizeMigratesLegacyLimitWarningThresholdToProviderSemantics()
    {
        var legacy = new AppSettings(
            RefreshCadence.FiveMinutes,
            IsWidgetVisible: true,
            IsWidgetAlwaysOnTop: false,
            WidgetLeft: 80,
            WidgetTop: 80,
            LimitWarningThresholdPercent: 20);

        var normalized = legacy.Normalize();

        Assert.Equal(20, normalized.GetLimitWarningSetting("codex").ThresholdPercent);
        Assert.Equal(80, normalized.GetLimitWarningSetting("claude").ThresholdPercent);
        Assert.Equal(80, normalized.GetLimitWarningSetting("gemini-pro").ThresholdPercent);
    }

    [Fact]
    public void NormalizeMigratesNonRecommendedLegacyThresholdAsCustom()
    {
        var legacy = new AppSettings(
            RefreshCadence.FiveMinutes,
            IsWidgetVisible: true,
            IsWidgetAlwaysOnTop: false,
            WidgetLeft: 80,
            WidgetTop: 80,
            LimitWarningThresholdPercent: 17);

        var normalized = legacy.Normalize();

        Assert.True(normalized.GetLimitWarningSetting("codex").IsCustom);
        Assert.True(normalized.GetLimitWarningSetting("claude").IsCustom);
        Assert.True(normalized.GetLimitWarningSetting("gemini-pro").IsCustom);
    }

    [Fact]
    public void NormalizeKeepsProviderLimitWarningSettingsIndependent()
    {
        var settings = AppSettings.Default with
        {
            LimitWarningSettings =
            [
                new ProviderLimitWarningSetting("codex", 12, IsCustom: true),
                new ProviderLimitWarningSetting("claude", 84),
                new ProviderLimitWarningSetting("gemini-pro", 73, IsCustom: true)
            ]
        };

        var normalized = settings.Normalize();

        Assert.Equal(new ProviderLimitWarningSetting("codex", 12, IsCustom: true), normalized.GetLimitWarningSetting("codex"));
        Assert.Equal(new ProviderLimitWarningSetting("claude", 84, IsCustom: true), normalized.GetLimitWarningSetting("claude"));
        Assert.Equal(new ProviderLimitWarningSetting("gemini-pro", 73, IsCustom: true), normalized.GetLimitWarningSetting("gemini-pro"));
    }

    [Fact]
    public void NormalizeClampsProviderLimitWarningSettings()
    {
        var settings = AppSettings.Default with
        {
            LimitWarningSettings =
            [
                new ProviderLimitWarningSetting("codex", 0, IsCustom: true),
                new ProviderLimitWarningSetting("claude", 100, IsCustom: true)
            ]
        };

        var normalized = settings.Normalize();

        Assert.Equal(1, normalized.GetLimitWarningSetting("codex").ThresholdPercent);
        Assert.Equal(99, normalized.GetLimitWarningSetting("claude").ThresholdPercent);
        Assert.Equal(90, normalized.GetLimitWarningSetting("gemini-pro").ThresholdPercent);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AiLimit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
