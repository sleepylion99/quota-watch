using AiLimit.App.Tray;
using AiLimit.Core.Domain;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class LimitWarningTests
{
    [Fact]
    public void FindWarningWarnsWhenRemainingLimitIsAtOrBelowTenPercent()
    {
        var warning = LimitWarningEvaluator.FindWarning([
            Snapshot(
                "codex",
                "ChatGPT Codex",
                [new UsageWindow("five-hour", "5-hour limit", 10, null, null, "high")])
        ]);

        Assert.NotNull(warning);
        Assert.Equal("ChatGPT Codex", warning!.ProviderName);
        Assert.Equal("5-hour limit", warning.WindowLabel);
        Assert.Equal(10, warning.Percent);
    }

    [Fact]
    public void FindWarningUsesConfiguredRemainingThreshold()
    {
        var warning = LimitWarningEvaluator.FindWarning(
            [
                new UsageSnapshot("codex", "ChatGPT Codex", DateTimeOffset.Now, UsageSource.Mock, UsageStatus.Fresh,
                    [new UsageWindow("five-hour", "5-hour limit", 20, null, null, "medium")])
            ],
            thresholdPercent: 20);

        Assert.NotNull(warning);
        Assert.Equal(20, warning!.Percent);
    }

    [Fact]
    public void FindWarningUsesConfiguredUsedThreshold()
    {
        var warning = LimitWarningEvaluator.FindWarning(
            [
                new UsageSnapshot("gemini-pro", "Google Antigravity", DateTimeOffset.Now, UsageSource.Mock, UsageStatus.Fresh,
                    [new UsageWindow("antigravity-gemini-3-5-flash-medium", "Gemini 3.5 Flash (Medium)", 80, null, null, "medium", IsUsedPercent: true)])
            ],
            thresholdPercent: 20);

        Assert.NotNull(warning);
        Assert.True(warning!.IsUsedPercent);
        Assert.Equal(80, warning.Percent);
    }

    [Fact]
    public void FindWarningWarnsWhenAntigravityUsedPercentIsAtOrAboveNinetyPercent()
    {
        var warning = LimitWarningEvaluator.FindWarning([
            Snapshot(
                "gemini-pro",
                "Google Antigravity",
                [new UsageWindow("antigravity-gemini-3-5-flash-medium", "Gemini 3.5 Flash (Medium)", 90, null, null, "medium", IsUsedPercent: true)])
        ]);

        Assert.NotNull(warning);
        Assert.Equal("Google Antigravity", warning!.ProviderName);
        Assert.Equal("Gemini 3.5 Flash (Medium)", warning.WindowLabel);
        Assert.Equal(90, warning.Percent);
        Assert.True(warning.IsUsedPercent);
    }

    [Fact]
    public void FindWarningUsesIndependentProviderThresholds()
    {
        var settings = new[]
        {
            new ProviderLimitWarningSetting("codex", 12, IsCustom: true),
            new ProviderLimitWarningSetting("claude", 84, IsCustom: true),
            new ProviderLimitWarningSetting("gemini-pro", 73, IsCustom: true)
        };
        var snapshots = new[]
        {
            Snapshot(
                "codex",
                "ChatGPT Codex",
                [new UsageWindow("five-hour", "5-hour limit", 13, null, null, "high")]),
            Snapshot(
                "claude",
                "Claude Code",
                [new UsageWindow("five-hour", "Current session", 84, null, null, "high", IsUsedPercent: true)]),
            Snapshot(
                "gemini-pro",
                "Google Antigravity",
                [new UsageWindow("antigravity-gemini", "Gemini", 72, null, null, "medium", IsUsedPercent: true)])
        };

        var warning = LimitWarningEvaluator.FindWarning(snapshots, providerThresholds: settings);

        Assert.NotNull(warning);
        Assert.Equal("claude", warning!.ProviderId);
        Assert.Equal(84, warning.Percent);
    }

    [Fact]
    public void FindWarningsReturnsEveryExceededProvider()
    {
        var snapshots = new[]
        {
            Snapshot(
                "codex",
                "ChatGPT Codex",
                [new UsageWindow("five-hour", "5-hour limit", 9, null, null, "high")]),
            Snapshot(
                "claude",
                "Claude Code",
                [new UsageWindow("five-hour", "Current session", 92, null, null, "high", IsUsedPercent: true)]),
            Snapshot(
                "gemini-pro",
                "Google Antigravity",
                [new UsageWindow("antigravity-gemini", "Gemini", 95, null, null, "high", IsUsedPercent: true)])
        };

        var warnings = LimitWarningEvaluator.FindWarnings(snapshots);

        Assert.Equal(3, warnings.Count);
        Assert.Equal(
            ["gemini-pro", "claude", "codex"],
            warnings.Select(warning => warning.ProviderId));
    }

    [Fact]
    public void UpdateWarningStateUsesIndependentProviderThresholds()
    {
        var warned = new HashSet<string>(StringComparer.Ordinal)
        {
            "codex:five-hour",
            "claude:five-hour"
        };
        var settings = new[]
        {
            new ProviderLimitWarningSetting("codex", 10),
            new ProviderLimitWarningSetting("claude", 90)
        };
        var snapshots = new[]
        {
            Snapshot(
                "codex",
                "ChatGPT Codex",
                [new UsageWindow("five-hour", "5-hour limit", 11, null, null, "high")]),
            Snapshot(
                "claude",
                "Claude Code",
                [new UsageWindow("five-hour", "Current session", 90, null, null, "high", IsUsedPercent: true)])
        };

        LimitWarningEvaluator.UpdateWarningState(snapshots, warned, settings);

        Assert.DoesNotContain("codex:five-hour", warned);
        Assert.Contains("claude:five-hour", warned);
    }

    [Fact]
    public void FindWarningIgnoresAlreadySeenWindowUntilItRecovers()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            "codex:five-hour"
        };

        var warning = LimitWarningEvaluator.FindWarning([
            Snapshot(
                "codex",
                "ChatGPT Codex",
                [new UsageWindow("five-hour", "5-hour limit", 8, null, null, "high")])
        ], seen);

        Assert.Null(warning);
    }

    [Fact]
    public void FindWarningCanSuppressMatchingWeeklyLimitWarning()
    {
        var resetAt = DateTimeOffset.Parse("2026-06-07T12:00:00+09:00");
        var warning = LimitWarningEvaluator.FindWarning(
            [
                Snapshot(
                    "codex",
                    "ChatGPT Codex",
                    [
                        new UsageWindow("weekly", "Weekly limit", 8, resetAt, null, "high"),
                        new UsageWindow("five-hour", "5-hour limit", 50, null, null, "high")
                    ],
                    "email:a@example.com")
            ],
            weeklySuppressions:
            [
                new WeeklyLimitWarningSuppression("codex", "weekly", resetAt, "email:a@example.com")
            ],
            now: DateTimeOffset.Parse("2026-06-01T12:00:00+09:00"));

        Assert.Null(warning);
    }

    [Fact]
    public void FindWarningStillShowsFiveHourWarningsWhenWeeklyWarningsAreSuppressed()
    {
        var warning = LimitWarningEvaluator.FindWarning(
            [
                Snapshot(
                    "codex",
                    "ChatGPT Codex",
                    [
                        new UsageWindow("weekly", "Weekly limit", 8, null, null, "high"),
                        new UsageWindow("five-hour", "5-hour limit", 7, null, null, "high")
                    ])
            ],
            weeklySuppressions:
            [
                new WeeklyLimitWarningSuppression("codex", "weekly")
            ]);

        Assert.NotNull(warning);
        Assert.Equal("five-hour", warning!.WindowId);
    }

    [Fact]
    public void FindWarningTreatsDetailedSecondaryWindowsAsWeeklyWarnings()
    {
        var warning = LimitWarningEvaluator.FindWarning(
            [
                Snapshot(
                    "codex",
                    "ChatGPT Codex",
                    [new UsageWindow("codex-spark-secondary", "Codex Spark weekly", 8, null, null, "high")])
            ],
            weeklySuppressions:
            [
                new WeeklyLimitWarningSuppression("codex", "codex-spark-secondary")
            ]);

        Assert.Null(warning);
    }

    [Fact]
    public void FindWarningDoesNotSuppressDifferentAccounts()
    {
        var resetAt = DateTimeOffset.Parse("2026-06-07T12:00:00+09:00");
        var warning = LimitWarningEvaluator.FindWarning(
            [
                Snapshot(
                    "codex",
                    "ChatGPT Codex",
                    [new UsageWindow("weekly", "Weekly limit", 8, resetAt, null, "high")],
                    "email:b@example.com")
            ],
            weeklySuppressions:
            [
                new WeeklyLimitWarningSuppression("codex", "weekly", resetAt, "email:a@example.com")
            ],
            now: DateTimeOffset.Parse("2026-06-01T12:00:00+09:00"));

        Assert.NotNull(warning);
    }

    [Fact]
    public void FindWarningDoesNotSuppressExpiredWeeklyReset()
    {
        var resetAt = DateTimeOffset.Parse("2026-05-30T12:00:00+09:00");
        var warning = LimitWarningEvaluator.FindWarning(
            [
                Snapshot(
                    "codex",
                    "ChatGPT Codex",
                    [new UsageWindow("weekly", "Weekly limit", 8, resetAt, null, "high")])
            ],
            weeklySuppressions:
            [
                new WeeklyLimitWarningSuppression("codex", "weekly", resetAt)
            ],
            now: DateTimeOffset.Parse("2026-06-01T12:00:00+09:00"));

        Assert.NotNull(warning);
    }

    [Fact]
    public void UpdateWarningStateRemovesRecoveredWindows()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            "codex:five-hour"
        };

        LimitWarningEvaluator.UpdateWarningState([
            Snapshot(
                "codex",
                "ChatGPT Codex",
                [new UsageWindow("five-hour", "5-hour limit", 50, null, null, "high")])
        ], seen);

        Assert.Empty(seen);
    }

    private static UsageSnapshot Snapshot(
        string id,
        string name,
        IReadOnlyList<UsageWindow> windows,
        string? accountKey = null)
    {
        return new UsageSnapshot(
            id,
            name,
            DateTimeOffset.Parse("2026-05-31T12:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            windows,
            AccountKey: accountKey);
    }
}
