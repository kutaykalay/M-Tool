using MTool.Core.Profiles;

namespace MTool.Core.Device;

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

    private static void RequireLength(IReadOnlyList<byte> values, int expected, string name)
    {
        if (values.Count != expected)
        {
            throw new ArgumentException($"Expected {expected} values, got {values.Count}.", name);
        }
    }
}
