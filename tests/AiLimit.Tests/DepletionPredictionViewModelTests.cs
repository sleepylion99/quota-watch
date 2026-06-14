using AiLimit.App.ViewModels;
using AiLimit.Core.Domain;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class DepletionPredictionViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-14T09:00:00Z");

    [Fact]
    public void UpdateShowsDepletionEtaAndSparklineForPrimaryWindow()
    {
        var prediction = new DepletionPrediction(
            PredictionState.WillDeplete,
            TimeSpan.FromMinutes(100),
            Now.AddMinutes(100),
            30,
            [
                Sample(Now.AddMinutes(-20), 40),
                Sample(Now.AddMinutes(-10), 45),
                Sample(Now, 50)
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [Snapshot()],
            false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            predictions: Predictions(prediction));

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowPrediction);
        Assert.Equal("About 1h 40m until depletion", provider.PredictionText);
        Assert.Contains("Expected around", provider.PredictionDetailText);
        Assert.NotEmpty(provider.SparklinePoints);
        Assert.NotEmpty(provider.SparklineProjectionPoints);
    }

    [Fact]
    public void UpdateShowsResetFirstMessageWithoutProjectionLine()
    {
        var prediction = new DepletionPrediction(
            PredictionState.ResetsFirst,
            null,
            null,
            1,
            [
                Sample(Now.AddMinutes(-20), 40),
                Sample(Now.AddMinutes(-10), 40.5),
                Sample(Now, 41)
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [Snapshot()],
            false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            predictions: Predictions(prediction));

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowPrediction);
        Assert.Equal("At current rate, won't hit this window's limit", provider.PredictionText);
        Assert.Empty(provider.PredictionDetailText);
        Assert.Empty(provider.SparklineProjectionPoints);
    }

    [Fact]
    public void UpdateShowsWaitingMessageForFlatUsage()
    {
        var prediction = new DepletionPrediction(
            PredictionState.WaitingForChange,
            null,
            null,
            0,
            [Sample(Now.AddMinutes(-3), 36), Sample(Now, 36)]);
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [Snapshot()],
            false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            predictions: Predictions(prediction));

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowPrediction);
        Assert.Equal("Waiting for usage change to estimate depletion", provider.PredictionText);
    }

    [Fact]
    public void UpdateShowsCollectionStatusWhenEstimateIsNotReady()
    {
        var prediction = new DepletionPrediction(
            PredictionState.Collecting,
            null,
            null,
            0,
            [Sample(Now, 50)]);
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [Snapshot()],
            false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            predictions: Predictions(prediction));

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowPrediction);
        Assert.Equal("Collecting usage data for prediction (1/3)", provider.PredictionText);
    }

    [Fact]
    public void UpdateShowsAlreadyDepletedForFullWindow()
    {
        var prediction = new DepletionPrediction(
            PredictionState.Depleted,
            TimeSpan.Zero,
            Now,
            0,
            [Sample(Now.AddMinutes(-3), 100), Sample(Now, 100)]);
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [Snapshot() with
            {
                Windows =
                [
                    new UsageWindow(
                        "five-hour",
                        "5-hour limit",
                        100,
                        Now.AddHours(3),
                        null,
                        "high",
                        IsUsedPercent: true)
                ]
            }],
            false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            predictions: Predictions(prediction));

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowPrediction);
        Assert.Equal("Limit already depleted", provider.PredictionText);
    }

    private static IReadOnlyDictionary<UsageWindowKey, DepletionPrediction> Predictions(
        DepletionPrediction prediction) =>
        new Dictionary<UsageWindowKey, DepletionPrediction>
        {
            [new("claude", "five-hour")] = prediction
        };

    private static UsageSnapshot Snapshot() =>
        new(
            "claude",
            "Claude Code",
            Now,
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5-hour limit", 50, Now.AddHours(3), null, "high", IsUsedPercent: true)]);

    private static UsageSample Sample(DateTimeOffset at, double percent) =>
        new("claude", "five-hour", at, percent);
}
