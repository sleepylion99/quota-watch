using AiLimit.Core.Refresh;

namespace AiLimit.Tests;

public sealed class RefreshSchedulerTests
{
    [Fact]
    public void NextRefreshAtAddsFiveMinutes()
    {
        var now = DateTimeOffset.Parse("2026-05-17T10:00:00+09:00");

        var next = RefreshScheduler.GetNextRefreshAt(now, RefreshCadence.FiveMinutes);

        Assert.Equal(now.AddMinutes(5), next);
    }

    [Fact]
    public void NextRefreshAtReturnsNullForManualCadence()
    {
        var now = DateTimeOffset.Parse("2026-05-17T10:00:00+09:00");

        var next = RefreshScheduler.GetNextRefreshAt(now, RefreshCadence.Manual);

        Assert.Null(next);
    }
}
