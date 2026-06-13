using AiLimit.App.Tray;
using AiLimit.Core.Domain;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class TrayStatusTextTests
{
    [Fact]
    public void BuildSummaryShowsLowestEnglishUsageWithProviderAndWindow()
    {
        var snapshots = new[]
        {
            Snapshot("codex", "ChatGPT Codex", 63, 41),
            ClaudeSnapshot(),
            Snapshot("gemini", "Gemini Pro", 88, 67)
        };

        var summary = TrayStatusText.BuildSummary(snapshots, AppLanguage.English);

        Assert.Equal("OK · Claude Opus 29%", summary);
    }

    [Fact]
    public void BuildSummaryShowsLowestKoreanUsageWithProviderAndWindow()
    {
        var snapshots = new[]
        {
            Snapshot("codex", "ChatGPT Codex", 63, 41),
            ClaudeSnapshot()
        };

        var summary = TrayStatusText.BuildSummary(snapshots, AppLanguage.Korean);

        Assert.Equal("OK · Claude Opus 29%", summary);
    }

    [Fact]
    public void BuildProviderLinesShowsKoreanUsageLines()
    {
        var snapshots = new[]
        {
            Snapshot("codex", "ChatGPT Codex", 63, 41),
            ClaudeSnapshot()
        };

        var lines = TrayStatusText.BuildProviderLines(snapshots, AppLanguage.Korean);

        Assert.Equal(
            ["Codex · 5시간 63%", "Claude · 5시간 70% / Sonnet 54% / Opus 29% / Routines 90% / Cowork 75%"],
            lines);
    }

    [Fact]
    public void BuildSummaryUsesAntigravityModelQuotaLabels()
    {
        var snapshots = new[]
        {
            Snapshot("codex", "ChatGPT Codex", 91, 85),
            new UsageSnapshot(
                "gemini-pro",
                "Google Antigravity",
                DateTimeOffset.Parse("2026-05-18T12:00:00+09:00"),
                UsageSource.Agent,
                UsageStatus.Fresh,
                [
                    new UsageWindow("antigravity-gemini-3-5-flash-medium", "Gemini 3.5 Flash (Medium)", 0, null, null, "medium"),
                    new UsageWindow("antigravity-claude-sonnet-4-6-thinking", "Claude Sonnet 4.6 (Thinking)", 20, null, null, "medium")
                ])
        };

        var summary = TrayStatusText.BuildSummary(snapshots, AppLanguage.English);
        var lines = TrayStatusText.BuildProviderLines(snapshots, AppLanguage.English);

        Assert.Equal("OK · Antigravity Claude Sonnet T 20% used", summary);
        Assert.Contains("Antigravity", lines);
        Assert.Contains("  Gemini Flash M 0%", lines);
        Assert.Contains("  Claude Sonnet T 20%", lines);
    }

    [Fact]
    public void BuildProviderLinesShowsAllAntigravityModelsOnSeparateTrayRows()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-05-18T12:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("antigravity-gemini-flash-high", "Gemini 3.5 Flash (High)", 0, null, null, "high"),
                new UsageWindow("antigravity-gemini-flash-low", "Gemini 3.5 Flash (Low)", 0, null, null, "low"),
                new UsageWindow("antigravity-gemini-flash-medium", "Gemini 3.5 Flash (Medium)", 0, null, null, "medium"),
                new UsageWindow("antigravity-gemini-pro-low", "Gemini 3.1 Pro (Low)", 0, null, null, "low"),
                new UsageWindow("antigravity-gemini-pro-high", "Gemini 3.1 Pro (High)", 0, null, null, "high"),
                new UsageWindow("antigravity-claude-sonnet-thinking", "Claude Sonnet 4.6 (Thinking)", 0, null, null, "medium"),
                new UsageWindow("antigravity-claude-opus-thinking", "Claude Opus 4.6 (Thinking)", 0, null, null, "medium"),
                new UsageWindow("antigravity-gpt-oss-medium", "GPT-OSS 120B (Medium)", 0, null, null, "medium")
            ]);

        var lines = TrayStatusText.BuildProviderLines([snapshot], AppLanguage.Korean);

        Assert.Equal(
            [
                "Antigravity",
                "  Gemini Flash M 0%",
                "  Gemini Flash H 0%",
                "  Gemini Flash L 0%",
                "  Gemini Pro H 0%",
                "  Gemini Pro L 0%",
                "  Claude Sonnet T 0%",
                "  Claude Opus T 0%",
                "  GPT-OSS M 0%"
            ],
            lines);
        Assert.All(lines, line => Assert.DoesNotContain("+", line));
    }

    [Fact]
    public void BuildProviderItemsKeepsAllCodexProWindows()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-06-06T12:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "5h limit", 6, null, null, "high"),
                new UsageWindow("weekly", "Weekly limit", 84, null, null, "high"),
                new UsageWindow("codex-spark-primary", "GPT-5.3-Codex-Spark 5h limit", 88, null, null, "high"),
                new UsageWindow("codex-spark-secondary", "GPT-5.3-Codex-Spark weekly limit", 96, null, null, "high"),
                new UsageWindow("code-review-credits", "Code review credits", 100, null, "Remaining credits: 7", "medium")
            ]);

        var item = Assert.Single(TrayStatusText.BuildProviderItems([snapshot], AppLanguage.Korean));

        Assert.Equal("codex", item.ProviderId);
        Assert.Equal("Codex · 5시간 6% 남음", item.Summary);
        Assert.Equal(
            [
                "5시간 · 6%",
                "주간 · 84%",
                "GPT-5.3-Codex-Spark 5h limit · 88%",
                "GPT-5.3-Codex-Spark weekly limit · 96%",
                "Code review credits · 7"
            ],
            item.Details);
    }

    [Fact]
    public void BuildProviderItemsKeepsAllClaudeMaxWindows()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-06T12:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "Current session", 70, null, null, "high"),
                new UsageWindow("weekly", "Current week (all models)", 60, null, null, "high"),
                new UsageWindow("weekly-sonnet", "Current week (Sonnet only)", 80, null, null, "high"),
                new UsageWindow("weekly-opus", "Current week (Opus only)", 50, null, null, "high"),
                new UsageWindow("weekly-routines", "Current week (Routines)", 90, null, null, "medium"),
                new UsageWindow("weekly-cowork", "Current week (Cowork)", 75, null, null, "medium")
            ]);

        var item = Assert.Single(TrayStatusText.BuildProviderItems([snapshot], AppLanguage.Korean));

        Assert.Equal("Claude · Opus 50% 남음", item.Summary);
        Assert.Equal(
            ["5시간 · 70%", "주간 · 60%", "Sonnet · 80%", "Opus · 50%", "Routines · 90%", "Cowork · 75%"],
            item.Details);
    }

    [Fact]
    public void BuildVisibleProviderLinesExpandsOnlySelectedProvider()
    {
        var items = new[]
        {
            new TrayProviderItem("codex", "Codex · 5시간 6%", ["5시간 · 6%", "주간 · 84%"]),
            new TrayProviderItem("claude", "Claude · Opus 50%", ["5시간 · 70%", "Opus · 50%"])
        };

        var lines = TrayStatusText.BuildVisibleProviderLines(items, "codex");

        Assert.Collection(
            lines,
            line =>
            {
                Assert.Equal("codex", line.ProviderId);
                Assert.Equal("Codex · 5시간 6%", line.Text);
                Assert.False(line.IsDetail);
            },
            line =>
            {
                Assert.Equal("codex", line.ProviderId);
                Assert.Equal("5시간 · 6%", line.Text);
                Assert.True(line.IsDetail);
            },
            line =>
            {
                Assert.Equal("codex", line.ProviderId);
                Assert.Equal("주간 · 84%", line.Text);
                Assert.True(line.IsDetail);
            },
            line =>
            {
                Assert.Equal("claude", line.ProviderId);
                Assert.Equal("Claude · Opus 50%", line.Text);
                Assert.False(line.IsDetail);
            });
    }

    [Fact]
    public void BuildVisibleProviderLinesSwitchesExpandedProvider()
    {
        var items = new[]
        {
            new TrayProviderItem("codex", "Codex · 5시간 6%", ["5시간 · 6%"]),
            new TrayProviderItem("claude", "Claude · Opus 50%", ["Opus · 50%"])
        };

        var lines = TrayStatusText.BuildVisibleProviderLines(items, "claude");

        Assert.Equal(
            ["Codex · 5시간 6%", "Claude · Opus 50%", "Opus · 50%"],
            lines.Select(line => line.Text).ToArray());
        Assert.False(lines[0].IsDetail);
        Assert.False(lines[1].IsDetail);
        Assert.True(lines[2].IsDetail);
    }

    [Fact]
    public void BuildSummaryShowsRefreshPromptWhenEmpty()
    {
        var summary = TrayStatusText.BuildSummary([], AppLanguage.English);

        Assert.Equal("Refresh to load usage", summary);
    }

    private static UsageSnapshot Snapshot(string id, string name, int fiveHourPercent, int weeklyPercent)
    {
        return new UsageSnapshot(
            id,
            name,
            DateTimeOffset.Parse("2026-05-18T12:00:00+09:00"),
            UsageSource.Mock,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "5-hour limit", fiveHourPercent, null, null, "high"),
                new UsageWindow("weekly", "Weekly limit", weeklyPercent, null, null, "medium")
            ]);
    }

    private static UsageSnapshot ClaudeSnapshot()
    {
        return new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-05-18T12:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "5-hour limit", 70, null, null, "high"),
                new UsageWindow("weekly-sonnet", "Sonnet weekly limit", 54, null, null, "high"),
                new UsageWindow("weekly-opus", "Opus weekly limit", 29, null, null, "high"),
                new UsageWindow("weekly-routines", "Daily Routines weekly limit", 90, null, null, "medium"),
                new UsageWindow("weekly-cowork", "Cowork weekly limit", 75, null, null, "medium")
            ]);
    }
}
