using AiLimit.App.Services;
using AiLimit.Core.Domain;

namespace AiLimit.Tests;

public sealed class AppStatePredictionTests
{
    [Fact]
    public void CreateUsageSamplesNormalizesUsedAndRemainingWindows()
    {
        var at = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var snapshots = new[]
        {
            Snapshot("codex", at, new UsageWindow("five-hour", "5h", 70, null, null, "high")),
            Snapshot("claude", at, new UsageWindow("five-hour", "5h", 70, null, null, "high", IsUsedPercent: true)),
            new UsageSnapshot("failed", "Failed", at, UsageSource.Agent, UsageStatus.Failed, [
                new UsageWindow("five-hour", "5h", 10, null, null, "high")
            ])
        };

        var samples = AppState.CreateUsageSamples(snapshots);

        Assert.Equal(2, samples.Count);
        Assert.Contains(samples, sample => sample.ProviderId == "codex" && sample.ConsumedPercent == 30);
        Assert.Contains(samples, sample => sample.ProviderId == "claude" && sample.ConsumedPercent == 70);
    }

    [Fact]
    public void CreateUsageSamplesPreservesPreciseProviderPercent()
    {
        var at = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var snapshots = new[]
        {
            Snapshot(
                "claude",
                at,
                new UsageWindow(
                    "weekly",
                    "Weekly",
                    36,
                    null,
                    null,
                    "high",
                    IsUsedPercent: true,
                    PrecisePercent: 36.125))
        };

        var sample = Assert.Single(AppState.CreateUsageSamples(snapshots));

        Assert.Equal(36.125, sample.ConsumedPercent);
    }

    [Fact]
    public void CreateUsageSamplesPreservesAccountKey()
    {
        var at = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            at,
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5h", 70, null, null, "high")],
            AccountKey: "codex:account-a");

        var sample = Assert.Single(AppState.CreateUsageSamples([snapshot]));

        Assert.Equal("codex:account-a", sample.AccountKey);
    }

    [Fact]
    public void BuildPredictionsKeysResultsByProviderAndWindow()
    {
        var now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var snapshot = Snapshot(
            "claude",
            now,
            new UsageWindow("five-hour", "5h", 50, now.AddHours(3), null, "high", IsUsedPercent: true));
        var history = new[]
        {
            new UsageSample("claude", "five-hour", now.AddMinutes(-20), 40),
            new UsageSample("claude", "five-hour", now.AddMinutes(-10), 45),
            new UsageSample("claude", "five-hour", now, 50)
        };

        var predictions = AppState.BuildPredictions([snapshot], history, now);

        var prediction = Assert.Single(predictions).Value;
        Assert.Equal(new UsageWindowKey("claude", "five-hour"), Assert.Single(predictions).Key);
        Assert.Equal(PredictionState.WillDeplete, prediction.State);
    }

    [Fact]
    public void BuildPredictionsKeepsCollectingStateVisible()
    {
        var now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var snapshot = Snapshot(
            "claude",
            now,
            new UsageWindow("five-hour", "5h", 50, now.AddHours(3), null, "high", IsUsedPercent: true));

        var predictions = AppState.BuildPredictions(
            [snapshot],
            [new UsageSample("claude", "five-hour", now, 50)],
            now);

        Assert.Equal(PredictionState.Collecting, Assert.Single(predictions).Value.State);
    }

    [Fact]
    public void BuildPredictionsDoesNotMixCodexAccountHistory()
    {
        var now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            now,
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5h", 50, now.AddHours(3), null, "high")],
            AccountKey: "codex:account-a");
        var history = new[]
        {
            new UsageSample("codex", "five-hour", now.AddMinutes(-20), 40, "codex:account-b"),
            new UsageSample("codex", "five-hour", now.AddMinutes(-10), 45, "codex:account-b"),
            new UsageSample("codex", "five-hour", now, 50, "codex:account-a")
        };

        var prediction = Assert.Single(AppState.BuildPredictions([snapshot], history, now)).Value;

        Assert.Equal(PredictionState.Collecting, prediction.State);
    }

    private static UsageSnapshot Snapshot(string providerId, DateTimeOffset at, UsageWindow window) =>
        new(providerId, providerId, at, UsageSource.Agent, UsageStatus.Fresh, [window]);
}
