using MTool.Core.Device;

namespace MTool.Core.Sensors;

public enum SensorStatus
{
    /// <summary>No successful poll yet.</summary>
    Waiting,

    /// <summary>Values are current (a single missed poll does not change this).</summary>
    Live,

    /// <summary>Several polls in a row failed; <see cref="SensorReading.Snapshot"/> is the last good one.</summary>
    Stale,

    /// <summary>EC access is paused (sleep/resume); the EC is not touched.</summary>
    Paused,
}

/// <param name="Snapshot">Last good values, or null before the first successful poll.</param>
/// <param name="LastUpdated">When <paramref name="Snapshot"/> was read.</param>
public sealed record SensorReading(SensorSnapshot? Snapshot, SensorStatus Status, int ConsecutiveMisses, DateTimeOffset? LastUpdated)
{
    public static SensorReading Initial { get; } = new(null, SensorStatus.Waiting, 0, null);

    /// <summary>A temperature the EC reported as implausible keeps its last good value.</summary>
    public static SensorSnapshot Merge(SensorSnapshot? previous, SensorSnapshot current) => previous is null
        ? current
        : current with
        {
            CpuTempC = current.CpuTempC ?? previous.CpuTempC,
            GpuTempC = current.GpuTempC ?? previous.GpuTempC,
        };
}
