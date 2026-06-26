namespace AiLimit.Core.Domain;

public static class DepletionPredictor
{
    public const int MinimumSampleCount = 3;
    public static readonly TimeSpan MinimumSampleSpan = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TrendHalfLife = TimeSpan.FromHours(2);
    public const double MinimumSlopePerHour = 0.2;
    public const double ResetDropThreshold = 5;

    public static DepletionPrediction Predict(
        IReadOnlyList<UsageSample> samples,
        double currentConsumedPercent,
        DateTimeOffset? resetAt,
        DateTimeOffset now)
    {
        var ordered = samples.OrderBy(sample => sample.AtUtc).ToList();
        if (currentConsumedPercent >= 100)
        {
            return new DepletionPrediction(
                PredictionState.Depleted,
                TimeSpan.Zero,
                now,
                0,
                ordered);
        }

        var segmentStart = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].ConsumedPercent - ordered[i].ConsumedPercent > ResetDropThreshold)
            {
                segmentStart = i;
            }
        }

        var trendSamples = ordered.Skip(segmentStart).ToList();
        if (trendSamples.Count < MinimumSampleCount
            || trendSamples[^1].AtUtc - trendSamples[0].AtUtc < MinimumSampleSpan)
        {
            return new DepletionPrediction(
                PredictionState.Collecting,
                null,
                null,
                0,
                trendSamples);
        }

        // Exponentially weighted least-squares regression. Recent samples dominate
        // (weight halves every TrendHalfLife) so a burst at the end of a long quiet
        // history surfaces immediately instead of being averaged into a flat trend.
        var origin = trendSamples[0].AtUtc;
        var decay = Math.Log(2) / TrendHalfLife.TotalHours;
        var nowHours = (now - origin).TotalHours;
        double sumW = 0, sumWX = 0, sumWY = 0;
        foreach (var sample in trendSamples)
        {
            var t = (sample.AtUtc - origin).TotalHours;
            var w = Math.Exp(-decay * (nowHours - t));
            sumW += w;
            sumWX += w * t;
            sumWY += w * sample.ConsumedPercent;
        }

        if (sumW <= 0)
        {
            return DepletionPrediction.None;
        }

        var xMean = sumWX / sumW;
        var yMean = sumWY / sumW;
        var numerator = 0d;
        var denominator = 0d;
        foreach (var sample in trendSamples)
        {
            var t = (sample.AtUtc - origin).TotalHours;
            var w = Math.Exp(-decay * (nowHours - t));
            var dx = t - xMean;
            numerator += w * dx * (sample.ConsumedPercent - yMean);
            denominator += w * dx * dx;
        }

        if (denominator <= 0)
        {
            return DepletionPrediction.None;
        }

        var slope = numerator / denominator;
        if (!double.IsFinite(slope) || slope < MinimumSlopePerHour)
        {
            return new DepletionPrediction(
                PredictionState.WaitingForChange,
                null,
                null,
                Math.Max(0, slope),
                trendSamples);
        }

        var hoursToFull = Math.Max(0, 100 - currentConsumedPercent) / slope;
        if (!double.IsFinite(hoursToFull))
        {
            return DepletionPrediction.None;
        }

        var depletionAt = now.AddHours(hoursToFull);
        if (resetAt is not null && depletionAt >= resetAt.Value)
        {
            return new DepletionPrediction(
                PredictionState.ResetsFirst,
                null,
                null,
                slope,
                trendSamples);
        }

        return new DepletionPrediction(
            PredictionState.WillDeplete,
            depletionAt - now,
            depletionAt,
            slope,
            trendSamples);
    }
}
