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
        Assert.Equal(30, prediction.Slope, 6);
        Assert.Equal(Now.AddHours(5.0 / 3.0), prediction.DepletionAt);
        Assert.Equal(TimeSpan.FromHours(5.0 / 3.0), prediction.TimeRemaining);
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
        Assert.Equal(30, prediction.Slope, 6);
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
        Assert.Equal(30, prediction.Slope, 6);
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
