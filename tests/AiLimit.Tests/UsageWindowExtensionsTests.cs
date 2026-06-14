using AiLimit.Core.Domain;

namespace AiLimit.Tests;

public sealed class UsageWindowExtensionsTests
{
    [Fact]
    public void UsedPercentWindowReturnsFieldAsConsumed()
    {
        var window = new UsageWindow("w", "Weekly", 30, null, null, "high", IsUsedPercent: true);
        Assert.Equal(30, window.ConsumedPercent());
    }

    [Fact]
    public void RemainingPercentWindowInvertsToConsumed()
    {
        var window = new UsageWindow("w", "5h", 30, null, null, "high", IsUsedPercent: false);
        Assert.Equal(70, window.ConsumedPercent());
    }
}
