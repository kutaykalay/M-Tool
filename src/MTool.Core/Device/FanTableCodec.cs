using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>Raw EC table bytes of one fan that M-Tool reads and writes: 6 up thresholds, 7 speeds.</summary>
public sealed record FanTables(IReadOnlyList<byte> UpThresholds, IReadOnlyList<byte> Speeds);

/// <summary>
/// Converts between EC fan tables and <see cref="FanCurve"/>. The down offsets are left out: they
/// stay at their factory values (<see cref="FactoryDefaults.CpuDownOffsets"/>,
/// <see cref="FactoryDefaults.GpuDownOffsets"/>).
/// </summary>
public static class FanTableCodec
{
    public static FanCurve Decode(IReadOnlyList<byte> upThresholds, IReadOnlyList<byte> speeds)
    {
        RequireLength(upThresholds, EcMap.ThresholdCount, nameof(upThresholds));
        RequireLength(speeds, EcMap.SpeedCount, nameof(speeds));

        var points = new FanPoint[EcMap.SpeedCount];
        points[0] = new FanPoint(0, speeds[0]);
        for (var i = 0; i < EcMap.ThresholdCount; i++)
        {
            points[i + 1] = new FanPoint(upThresholds[i], speeds[i + 1]);
        }

        return new FanCurve(Array.AsReadOnly(points));
    }

    public static FanTables Encode(FanCurve curve)
    {
        if (curve.Points.Count != EcMap.SpeedCount)
        {
            throw new ArgumentException($"Expected {EcMap.SpeedCount} points, got {curve.Points.Count}.", nameof(curve));
        }

        var steps = curve.Points.Skip(1).ToArray();
        return new FanTables(
            UpThresholds: Array.AsReadOnly(steps.Select(p => ToByte(p.UpThresholdC)).ToArray()),
            Speeds: Array.AsReadOnly(curve.Points.Select(p => ToByte(p.SpeedPercent)).ToArray()));
    }

    private static byte ToByte(int value) => value is >= byte.MinValue and <= byte.MaxValue
        ? (byte)value
        : throw new ArgumentOutOfRangeException(nameof(value), value, "Fan table values must fit in a byte.");

    private static void RequireLength(IReadOnlyList<byte> values, int expected, string name)
    {
        if (values.Count != expected)
        {
            throw new ArgumentException($"Expected {expected} values, got {values.Count}.", name);
        }
    }
}
