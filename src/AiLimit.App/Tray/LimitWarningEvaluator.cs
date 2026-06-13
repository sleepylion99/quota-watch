using AiLimit.Core.Domain;
using AiLimit.Core.Settings;

namespace AiLimit.App.Tray;

public sealed record LimitWarning(
    string ProviderId,
    string ProviderName,
    string WindowId,
    string WindowLabel,
    int Percent,
    bool IsUsedPercent,
    DateTimeOffset? ResetAt,
    string? AccountKey)
{
    public string Key => $"{ProviderId}:{WindowId}";

    public bool IsWeeklyLimit =>
        WindowId.Equals("weekly", StringComparison.OrdinalIgnoreCase)
        || WindowId.StartsWith("weekly-", StringComparison.OrdinalIgnoreCase)
        || WindowId.EndsWith("-secondary", StringComparison.OrdinalIgnoreCase)
        || WindowLabel.Contains("weekly", StringComparison.OrdinalIgnoreCase);
}

public static class LimitWarningEvaluator
{
    private const int RemainingWarningPercent = 10;
    private const int UsedWarningPercent = 90;

    public static LimitWarning? FindWarning(
        IReadOnlyList<UsageSnapshot> snapshots,
        ISet<string>? alreadyWarned = null,
        IReadOnlyList<WeeklyLimitWarningSuppression>? weeklySuppressions = null,
        DateTimeOffset? now = null,
        int thresholdPercent = RemainingWarningPercent,
        IReadOnlyList<ProviderLimitWarningSetting>? providerThresholds = null)
    {
        return FindWarnings(
            snapshots,
            alreadyWarned,
            weeklySuppressions,
            now,
            thresholdPercent,
            providerThresholds).FirstOrDefault();
    }

    public static IReadOnlyList<LimitWarning> FindWarnings(
        IReadOnlyList<UsageSnapshot> snapshots,
        ISet<string>? alreadyWarned = null,
        IReadOnlyList<WeeklyLimitWarningSuppression>? weeklySuppressions = null,
        DateTimeOffset? now = null,
        int thresholdPercent = RemainingWarningPercent,
        IReadOnlyList<ProviderLimitWarningSetting>? providerThresholds = null)
    {
        thresholdPercent = NormalizeThreshold(thresholdPercent);
        var resolvedNow = now ?? DateTimeOffset.Now;
        return snapshots
            .Where(snapshot => snapshot.Status == UsageStatus.Fresh)
            .SelectMany(snapshot => snapshot.Windows.Select(window =>
                ToWarning(snapshot, window, thresholdPercent, providerThresholds)))
            .Where(warning => warning is not null)
            .Select(warning => warning!)
            .Where(warning => !IsSuppressed(warning, weeklySuppressions, resolvedNow))
            .Where(warning => alreadyWarned is null || !alreadyWarned.Contains(warning.Key))
            .OrderByDescending(warning => warning.IsUsedPercent ? warning.Percent : 100 - warning.Percent)
            .ToList();
    }

    public static void UpdateWarningState(
        IReadOnlyList<UsageSnapshot> snapshots,
        ISet<string> alreadyWarned,
        IReadOnlyList<ProviderLimitWarningSetting>? providerThresholds = null,
        int thresholdPercent = RemainingWarningPercent)
    {
        var activeWarningKeys = snapshots
            .Where(snapshot => snapshot.Status == UsageStatus.Fresh)
            .SelectMany(snapshot => snapshot.Windows.Select(window =>
                ToWarning(snapshot, window, NormalizeThreshold(thresholdPercent), providerThresholds)))
            .Where(warning => warning is not null)
            .Select(warning => warning!.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in alreadyWarned.Where(key => !activeWarningKeys.Contains(key)).ToList())
        {
            alreadyWarned.Remove(key);
        }
    }

    private static LimitWarning? ToWarning(
        UsageSnapshot snapshot,
        UsageWindow window,
        int legacyRemainingThreshold,
        IReadOnlyList<ProviderLimitWarningSetting>? providerThresholds)
    {
        var isUsedPercent = IsUsedPercentWindow(window);
        var thresholdPercent = ResolveThreshold(
            snapshot.ProviderId,
            isUsedPercent,
            legacyRemainingThreshold,
            providerThresholds);
        var shouldWarn = isUsedPercent
            ? window.PercentRemaining >= thresholdPercent
            : window.PercentRemaining <= thresholdPercent;
        if (!shouldWarn)
        {
            return null;
        }

        return new LimitWarning(
            snapshot.ProviderId,
            snapshot.DisplayName,
            window.Id,
            window.Label,
            window.PercentRemaining,
            isUsedPercent,
            window.ResetAt,
            snapshot.AccountKey);
    }

    private static bool IsSuppressed(
        LimitWarning warning,
        IReadOnlyList<WeeklyLimitWarningSuppression>? suppressions,
        DateTimeOffset now)
    {
        if (!warning.IsWeeklyLimit || suppressions is null || suppressions.Count == 0)
        {
            return false;
        }

        return suppressions.Any(suppression =>
            suppression.ProviderId.Equals(warning.ProviderId, StringComparison.Ordinal)
            && suppression.WindowId.Equals(warning.WindowId, StringComparison.Ordinal)
            && AccountMatches(suppression.AccountKey, warning.AccountKey)
            && ResetMatches(suppression.ResetAt, warning.ResetAt, now));
    }

    private static bool AccountMatches(string? suppressionAccountKey, string? warningAccountKey)
    {
        return string.IsNullOrWhiteSpace(suppressionAccountKey)
            ? string.IsNullOrWhiteSpace(warningAccountKey)
            : suppressionAccountKey.Equals(warningAccountKey, StringComparison.Ordinal);
    }

    private static bool ResetMatches(DateTimeOffset? suppressionResetAt, DateTimeOffset? warningResetAt, DateTimeOffset now)
    {
        if (suppressionResetAt is null)
        {
            return true;
        }

        return suppressionResetAt > now
            && warningResetAt is not null
            && suppressionResetAt.Value.Equals(warningResetAt.Value);
    }

    private static bool IsUsedPercentWindow(UsageWindow window)
    {
        return window.IsUsedPercent;
    }

    private static int NormalizeThreshold(int thresholdPercent)
    {
        return Math.Clamp(thresholdPercent, 1, 99);
    }

    private static int ResolveThreshold(
        string providerId,
        bool isUsedPercent,
        int legacyRemainingThreshold,
        IReadOnlyList<ProviderLimitWarningSetting>? providerThresholds)
    {
        var configured = providerThresholds?.LastOrDefault(setting =>
            setting.ProviderId.Equals(providerId, StringComparison.Ordinal));
        if (configured is not null)
        {
            return NormalizeThreshold(configured.ThresholdPercent);
        }

        return isUsedPercent
            ? 100 - legacyRemainingThreshold
            : legacyRemainingThreshold;
    }
}
