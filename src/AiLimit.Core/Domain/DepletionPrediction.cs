namespace AiLimit.Core.Domain;

public enum PredictionState
{
    None,
    Collecting,
    WaitingForChange,
    Depleted,
    WillDeplete,
    ResetsFirst
}

public readonly record struct UsageWindowKey(string ProviderId, string WindowId);

public sealed record DepletionPrediction(
    PredictionState State,
    TimeSpan? TimeRemaining,
    DateTimeOffset? DepletionAt,
    double Slope,
    IReadOnlyList<UsageSample> TrendSamples)
{
    public static DepletionPrediction None { get; } =
        new(PredictionState.None, null, null, 0, []);
}
