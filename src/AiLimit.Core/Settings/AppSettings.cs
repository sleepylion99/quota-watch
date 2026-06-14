using System.Globalization;
using AiLimit.Core.Providers;
using AiLimit.Core.Refresh;

namespace AiLimit.Core.Settings;

public enum LimitDisplayMode
{
    FiveHourOnly,
    WeeklyOnly,
    BothText,
    Bars
}

public enum AppLanguage
{
    System,
    English,
    Korean,
    Japanese,
    Chinese
}

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public sealed record AppLanguageCatalogItem(
    AppLanguage Language,
    string Code,
    string EnglishName,
    string KoreanName,
    string JapaneseName,
    string ChineseName);

public static class AppLanguageCatalog
{
    public static IReadOnlyList<AppLanguageCatalogItem> SupportedLanguages { get; } =
    [
        new(AppLanguage.System, "AUTO", "System default", "시스템 기본", "システム既定", "系统默认"),
        new(AppLanguage.Korean, "KO", "Korean", "한국어", "韓国語", "韩语"),
        new(AppLanguage.English, "EN", "English", "영어", "英語", "英语"),
        new(AppLanguage.Japanese, "JA", "Japanese", "일본어", "日本語", "日语"),
        new(AppLanguage.Chinese, "ZH", "Chinese", "중국어", "中国語", "中文")
    ];

    public static string LabelFor(AppLanguageCatalogItem item, AppLanguage displayLanguage)
    {
        return AppLanguageResolver.Resolve(displayLanguage) switch
        {
            AppLanguage.Korean => item.KoreanName,
            AppLanguage.Japanese => item.JapaneseName,
            AppLanguage.Chinese => item.ChineseName,
            _ => item.EnglishName
        };
    }
}

public sealed record ProviderSetting(
    string Id,
    string DisplayName,
    bool IsEnabled,
    string? Mode = null)
{
    public static string? NormalizeMode(string providerId, string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        var normalized = mode.Trim().ToLowerInvariant();
        return providerId switch
        {
            "codex" when normalized is "auto" or "basic" or "pro" => normalized,
            _ => null
        };
    }
}

public sealed record WeeklyLimitWarningSuppression(
    string ProviderId,
    string WindowId,
    DateTimeOffset? ResetAt = null,
    string? AccountKey = null);

public sealed record ProviderLimitWarningSetting(
    string ProviderId,
    int ThresholdPercent,
    bool IsCustom = false);

public sealed record AppSettings(
    RefreshCadence RefreshCadence,
    bool IsWidgetVisible,
    bool IsWidgetAlwaysOnTop,
    double WidgetLeft,
    double WidgetTop,
    LimitDisplayMode LimitDisplayMode = LimitDisplayMode.BothText,
    AppLanguage Language = AppLanguage.System,
    bool IsLimitWarningEnabled = true,
    int LimitWarningThresholdPercent = 10,
    IReadOnlyList<WeeklyLimitWarningSuppression>? WeeklyLimitWarningSuppressions = null,
    IReadOnlyList<ProviderSetting>? Providers = null,
    IReadOnlyList<ProviderLimitWarningSetting>? LimitWarningSettings = null,
    AppThemeMode ThemeMode = AppThemeMode.Dark,
    double DashboardOpacity = 1.0,
    double WidgetOpacity = 1.0,
    bool IsDashboardAlwaysOnTop = false)
{
    public const double MinimumWindowOpacity = 0.2;
    public const double MaximumWindowOpacity = 1.0;

    public static AppSettings Default { get; } = new(
        RefreshCadence.FiveMinutes,
        IsWidgetVisible: true,
        IsWidgetAlwaysOnTop: false,
        WidgetLeft: 80,
        WidgetTop: 80,
        LimitDisplayMode: LimitDisplayMode.BothText,
        Language: AppLanguage.System,
        IsLimitWarningEnabled: true,
        LimitWarningThresholdPercent: 10,
        WeeklyLimitWarningSuppressions: [],
        Providers: ProviderCatalog.KnownProviders
            .Select(provider => new ProviderSetting(provider.Id, provider.DisplayName, provider.IsEnabledByDefault))
            .ToList(),
        LimitWarningSettings:
        [
            new ProviderLimitWarningSetting("codex", 10),
            new ProviderLimitWarningSetting("claude", 90),
            new ProviderLimitWarningSetting("gemini-pro", 90)
        ],
        ThemeMode: AppThemeMode.System,
        DashboardOpacity: 1.0,
        WidgetOpacity: 1.0,
        IsDashboardAlwaysOnTop: false);

    public IReadOnlyList<ProviderSetting> GetEffectiveProviders()
    {
        var savedById = (Providers ?? []).ToDictionary(provider => provider.Id);
        return ProviderCatalog.KnownProviders
            .Select(provider => savedById.TryGetValue(provider.Id, out var saved)
                ? saved with
                {
                    DisplayName = provider.DisplayName,
                    Mode = ProviderSetting.NormalizeMode(provider.Id, saved.Mode)
                }
                : new ProviderSetting(provider.Id, provider.DisplayName, provider.IsEnabledByDefault))
            .ToList();
    }

    public AppSettings Normalize()
    {
        var legacyThreshold = ClampPercent(LimitWarningThresholdPercent);
        return this with
        {
            Providers = GetEffectiveProviders(),
            LimitWarningThresholdPercent = legacyThreshold,
            LimitWarningSettings = GetEffectiveLimitWarningSettings(legacyThreshold),
            WeeklyLimitWarningSuppressions = WeeklyLimitWarningSuppressions ?? [],
            ThemeMode = NormalizeThemeMode(ThemeMode),
            DashboardOpacity = ClampOpacity(DashboardOpacity),
            WidgetOpacity = ClampOpacity(WidgetOpacity)
        };
    }

    public static double ClampOpacity(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return MaximumWindowOpacity;
        }

        return Math.Clamp(value, MinimumWindowOpacity, MaximumWindowOpacity);
    }

    private static AppThemeMode NormalizeThemeMode(AppThemeMode value)
    {
        return value is AppThemeMode.System or AppThemeMode.Light or AppThemeMode.Dark
            ? value
            : AppThemeMode.System;
    }

    public ProviderLimitWarningSetting GetLimitWarningSetting(string providerId)
    {
        var legacyThreshold = ClampPercent(LimitWarningThresholdPercent);
        return GetEffectiveLimitWarningSettings(legacyThreshold)
            .First(setting => setting.ProviderId.Equals(providerId, StringComparison.Ordinal));
    }

    private IReadOnlyList<ProviderLimitWarningSetting> GetEffectiveLimitWarningSettings(int legacyThreshold)
    {
        var savedById = (LimitWarningSettings ?? [])
            .GroupBy(setting => setting.ProviderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        return new[] { "codex", "claude", "gemini-pro" }
            .Select(providerId =>
            {
                if (savedById.TryGetValue(providerId, out var saved))
                {
                    var threshold = ClampPercent(saved.ThresholdPercent);
                    return saved with
                    {
                        ThresholdPercent = threshold,
                        IsCustom = saved.IsCustom || !IsRecommendedThreshold(providerId, threshold)
                    };
                }

                var migratedThreshold = providerId == "codex"
                    ? legacyThreshold
                    : 100 - legacyThreshold;
                migratedThreshold = ClampPercent(migratedThreshold);
                return new ProviderLimitWarningSetting(
                    providerId,
                    migratedThreshold,
                    IsCustom: !IsRecommendedThreshold(providerId, migratedThreshold));
            })
            .ToList();
    }

    private static int ClampPercent(int percent) => Math.Clamp(percent, 1, 99);

    private static bool IsRecommendedThreshold(string providerId, int percent)
    {
        return providerId == "codex"
            ? percent is 10 or 20 or 30
            : percent is 90 or 80 or 70;
    }

    public AppSettings PruneExpiredWeeklyLimitWarningSuppressions(DateTimeOffset now)
    {
        var current = WeeklyLimitWarningSuppressions ?? [];
        var pruned = current
            .Where(suppression => suppression.ResetAt is null || suppression.ResetAt > now)
            .Distinct()
            .ToList();

        return pruned.Count == current.Count
            ? this
            : this with { WeeklyLimitWarningSuppressions = pruned };
    }
}

public static class AppLanguageResolver
{
    public static AppLanguage Resolve(AppLanguage language)
    {
        if (language != AppLanguage.System)
        {
            return language;
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ko" => AppLanguage.Korean,
            "ja" => AppLanguage.Japanese,
            "zh" => AppLanguage.Chinese,
            _ => AppLanguage.English
        };
    }
}
