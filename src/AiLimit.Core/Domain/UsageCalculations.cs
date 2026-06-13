namespace AiLimit.Core.Domain;

internal static class UsageCalculations
{
    internal static int PercentRemaining(double usedPercent) =>
        Math.Clamp(100 - (int)Math.Round(usedPercent, MidpointRounding.AwayFromZero), 0, 100);
}
