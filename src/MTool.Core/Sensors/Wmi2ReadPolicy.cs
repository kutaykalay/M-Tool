namespace MTool.Core.Sensors;

/// <summary>How often WMI2 may be read live: never more often than every 2 seconds.</summary>
public static class Wmi2ReadPolicy
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);

    /// <summary>The requested interval, or <see cref="MinInterval"/> when it is shorter.</summary>
    public static TimeSpan Interval(TimeSpan requested) => requested < MinInterval ? MinInterval : requested;
}
