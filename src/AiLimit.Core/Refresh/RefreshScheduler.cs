namespace AiLimit.Core.Refresh;

public static class RefreshScheduler
{
    public static DateTimeOffset? GetNextRefreshAt(DateTimeOffset checkedAt, RefreshCadence cadence)
    {
        return cadence switch
        {
            RefreshCadence.Manual => null,
            RefreshCadence.OneMinute => checkedAt.AddMinutes(1),
            RefreshCadence.FiveMinutes => checkedAt.AddMinutes(5),
            RefreshCadence.FifteenMinutes => checkedAt.AddMinutes(15),
            RefreshCadence.ThirtyMinutes => checkedAt.AddMinutes(30),
            _ => checkedAt.AddMinutes(5)
        };
    }

    public static TimeSpan? ToInterval(RefreshCadence cadence)
    {
        return cadence switch
        {
            RefreshCadence.Manual => null,
            RefreshCadence.OneMinute => TimeSpan.FromMinutes(1),
            RefreshCadence.FiveMinutes => TimeSpan.FromMinutes(5),
            RefreshCadence.FifteenMinutes => TimeSpan.FromMinutes(15),
            RefreshCadence.ThirtyMinutes => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromMinutes(5)
        };
    }
}
