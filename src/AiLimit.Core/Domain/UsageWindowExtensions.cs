namespace AiLimit.Core.Domain;

public static class UsageWindowExtensions
{
    /// Consumed % (rises 0->100). IsUsedPercent => field is used %, else it is remaining % so invert.
    public static double ConsumedPercent(this UsageWindow window)
    {
        var percent = window.PrecisePercent ?? window.PercentRemaining;
        return window.IsUsedPercent ? percent : 100 - percent;
    }
}
