using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>Raw EC table bytes of one fan: 6 up thresholds, 7 speeds, 6 down offsets.</summary>
public sealed record FanTables(IReadOnlyList<byte> UpThresholds, IReadOnlyList<byte> Speeds, IReadOnlyList<byte> DownOffsets);

/// <summary>
/// Converts between EC fan tables and <see cref="FanCurve"/>. The EC stores each down threshold
/// as an offset below the matching up threshold.
/// </summary>
public static class FanTableCodec
{
    public static FanCurve Decode(
        IReadOnlyList<byte> upThresholds,
        IReadOnlyList<byte> speeds,
        IReadOnlyList<byte> downOffsets)
    {
        RequireLength(upThresholds, EcMap.ThresholdCount, nameof(upThresholds));
        RequireLength(speeds, EcMap.SpeedCount, nameof(speeds));
        RequireLength(downOffsets, EcMap.ThresholdCount, nameof(downOffsets));

        var points = new FanPoint[EcMap.SpeedCount];
        points[0] = new FanPoint(0, 0, speeds[0]);
        for (var i = 0; i < EcMap.ThresholdCount; i++)
        {
            var up = upThresholds[i];
            points[i + 1] = new FanPoint(up, up - downOffsets[i], speeds[i + 1]);
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
            Speeds: Array.AsReadOnly(curve.Points.Select(p => ToByte(p.SpeedPercent)).ToArray()),
            DownOffsets: Array.AsReadOnly(steps.Select(p => ToByte(p.UpThresholdC - p.DownThresholdC)).ToArray()));
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
