using AiLimit.Core.Domain;

namespace AiLimit.Tests;

public sealed class DepletionPredictorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");

    [Fact]
    public void PredictsDepletionFromRisingUsage()
    {
        var samples = Samples((0, 40), (10, 45), (20, 50));

        var prediction = DepletionPredictor.Predict(samples, 50, Now.AddHours(3), Now);

        Assert.Equal(PredictionState.WillDeplete, prediction.State);
        Assert.InRange(prediction.Slope, 29, 31);
        Assert.NotNull(prediction.TimeRemaining);
        Assert.InRange(prediction.TimeRemaining!.Value.TotalHours, 1.6, 1.7);
    }

    [Fact]
    public void WaitsForChangeForFlatOrFallingUsage()
    {
        Assert.Equal(
            PredictionState.WaitingForChange,
            DepletionPredictor.Predict(Samples((0, 50), (10, 50), (20, 50)), 50, null, Now).State);
        Assert.Equal(
            PredictionState.WaitingForChange,
            DepletionPredictor.Predict(Samples((0, 55), (10, 52), (20, 50)), 50, null, Now).State);
    }

    [Fact]
    public void ReportsCollectingUntilThreeSamplesSpanFiveMinutes()
    {
        Assert.Equal(
            PredictionState.Collecting,
            DepletionPredictor.Predict(Samples((0, 40)), 40, Now.AddHours(3), Now).State);
        Assert.Equal(
            PredictionState.Collecting,
            DepletionPredictor.Predict(Samples((0, 40), (3, 45)), 45, Now.AddHours(3), Now).State);
        // 3 samples but spanning only 4 minutes — still collecting.
        Assert.Equal(
            PredictionState.Collecting,
            DepletionPredictor.Predict(Samples((0, 40), (2, 42), (4, 44)), 44, Now.AddHours(3), Now).State);
    }

    [Fact]
    public void PredictsFromThreeSamplesSpanningFiveMinutes()
    {
        var prediction = DepletionPredictor.Predict(
            Samples((0, 40), (3, 42), (5, 43)),
            43,
            Now.AddHours(3),
            Now);

        Assert.Equal(PredictionState.WillDeplete, prediction.State);
        Assert.InRange(prediction.Slope, 35, 37);
    }

    [Fact]
    public void WaitsForChangeWhenUsageIsFlat()
    {
        var prediction = DepletionPredictor.Predict(
            Samples((0, 50), (3, 50), (5, 50)),
            50,
            Now.AddHours(3),
            Now);

        Assert.Equal(PredictionState.WaitingForChange, prediction.State);
    }

    [Fact]
    public void ReportsAlreadyDepletedAtOneHundredPercent()
    {
        var prediction = DepletionPredictor.Predict(
            Samples((0, 100), (3, 100)),
            100,
            Now.AddHours(3),
            Now);

        Assert.Equal(PredictionState.Depleted, prediction.State);
        Assert.Equal(TimeSpan.Zero, prediction.TimeRemaining);
        Assert.Equal(Now, prediction.DepletionAt);
    }

    [Fact]
    public void UsesOnlySamplesAfterLatestResetDrop()
    {
        var samples = Samples((0, 80), (10, 90), (20, 5), (30, 10), (40, 15));

        var prediction = DepletionPredictor.Predict(samples, 15, Now.AddHours(4), Now);

        Assert.Equal(PredictionState.WillDeplete, prediction.State);
        Assert.InRange(prediction.Slope, 29, 31);
        Assert.Equal(3, prediction.TrendSamples.Count);
        Assert.Equal(5, prediction.TrendSamples[0].ConsumedPercent);
    }

    [Fact]
    public void ReportsResetFirstWhenWindowResetsBeforeProjectedDepletion()
    {
        var samples = Samples((0, 40), (10, 45), (20, 50));

        var prediction = DepletionPredictor.Predict(samples, 50, Now.AddHours(1), Now);

        Assert.Equal(PredictionState.ResetsFirst, prediction.State);
        Assert.Null(prediction.TimeRemaining);
        Assert.Null(prediction.DepletionAt);
        Assert.InRange(prediction.Slope, 29, 31);
    }

    [Fact]
    public void RecentBurstDominatesLongQuietHistory()
    {
        // Weekly-style scenario: 6 days of near-zero usage (~0.2 %/h) followed by a
        // sudden 3-hour burst (~7 %/h) lifting consumption from 30 % to 52 %. A plain
        // linear regression dilutes the burst across the whole week and predicts
        // ResetsFirst; the EWMA-weighted regression weights the recent burst and
        // surfaces a concrete depletion ETA.
        var samples = new List<UsageSample>();
        for (var hoursAgo = 144; hoursAgo >= 6; hoursAgo -= 6)
        {
            samples.Add(new UsageSample(
                "claude",
                "weekly",
                Now.AddHours(-hoursAgo),
                30.0 * (144 - hoursAgo) / 144));
        }
        samples.Add(new UsageSample("claude", "weekly", Now.AddHours(-3), 30));
        samples.Add(new UsageSample("claude", "weekly", Now.AddHours(-2), 38));
        samples.Add(new UsageSample("claude", "weekly", Now.AddHours(-1), 45));
        samples.Add(new UsageSample("claude", "weekly", Now, 52));

        var prediction = DepletionPredictor.Predict(samples, 52, Now.AddHours(20), Now);

        Assert.Equal(PredictionState.WillDeplete, prediction.State);
        Assert.NotNull(prediction.TimeRemaining);
        Assert.InRange(prediction.TimeRemaining!.Value.TotalHours, 4, 14);
    }

    private static IReadOnlyList<UsageSample> Samples(params (int Minutes, double Percent)[] values)
    {
        var start = Now.AddMinutes(-values[^1].Minutes);
        return values
            .Select(value => new UsageSample(
                "claude",
                "five-hour",
                start.AddMinutes(value.Minutes),
                value.Percent))
            .ToList();
    }
}
