using MTool.Core.Ec;

namespace MTool.Core.Profiles;

/// <summary>Where one point of a fan curve may go. Min above max means the curve is already invalid there.</summary>
public sealed record PointLimits(int MinUpC, int MaxUpC, int MinSpeedPercent, int MaxSpeedPercent);

/// <summary>
/// Drag rules for the fan curve editor: a point stays inside the single-point rules of
/// <see cref="CurveValidator"/>. The envelope speed spans several points, so it is left to the validator.
/// </summary>
public static class CurveEditing
{
    /// <param name="downOffsets">The fan's factory down offsets, one per step after the idle point.</param>
    /// <exception cref="ArgumentException">The curve is not seven points or the offsets do not match it.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a point of the curve.</exception>
    public static PointLimits Limits(FanCurve curve, int index, IReadOnlyList<int> downOffsets)
    {
        Check(curve, index, downOffsets);
        var points = curve.Points;
        var last = CurveValidator.PointCount - 1;

        var minSpeed = index == 0 ? 0 : Math.Max(0, points[index - 1].SpeedPercent);
        var maxSpeed = index == last
            ? EcWriteRules.MaxSpeedPercent
            : Math.Min(EcWriteRules.MaxSpeedPercent, points[index + 1].SpeedPercent);
        if (index == last)
        {
            minSpeed = Math.Max(minSpeed, CurveValidator.SafetyFloorMinLastSpeedPercent);
        }

        if (index == 0)
        {
            return new PointLimits(0, 0, minSpeed, maxSpeed);
        }

        // Each step must drop back above the step below it: up[i] - offset > up[i - 1].
        var minUp = Math.Max(EcWriteRules.MinUpThresholdC, points[index - 1].UpThresholdC + downOffsets[index - 1] + 1);
        var maxUp = index == last ? CurveValidator.SafetyFloorMaxLastThresholdC
            : index == 1 ? CurveValidator.MaxFirstStepThresholdC
            : EcWriteRules.MaxUpThresholdC;
        if (index < last)
        {
            maxUp = Math.Min(maxUp, points[index + 1].UpThresholdC - downOffsets[index] - 1);
        }

        return new PointLimits(minUp, maxUp, minSpeed, maxSpeed);
    }

    /// <summary>
    /// A new curve with point <paramref name="index"/> moved to the nearest allowed place. On an axis
    /// whose range is empty the point keeps its current value.
    /// </summary>
    /// <inheritdoc cref="Limits" path="/exception"/>
    public static FanCurve Move(FanCurve curve, int index, int upC, int speedPercent, IReadOnlyList<int> downOffsets)
    {
        var limits = Limits(curve, index, downOffsets);
        var current = curve.Points[index];
        var moved = new FanPoint(
            Clamp(upC, limits.MinUpC, limits.MaxUpC, current.UpThresholdC),
            Clamp(speedPercent, limits.MinSpeedPercent, limits.MaxSpeedPercent, current.SpeedPercent));

        var points = curve.Points.ToArray();
        points[index] = moved;
        return new FanCurve(Array.AsReadOnly(points));
    }

    private static int Clamp(int value, int min, int max, int current) => min > max ? current : Math.Clamp(value, min, max);

    private static void Check(FanCurve curve, int index, IReadOnlyList<int> downOffsets)
    {
        if (curve.Points.Count != CurveValidator.PointCount)
        {
            throw new ArgumentException($"Expected {CurveValidator.PointCount} points, got {curve.Points.Count}.", nameof(curve));
        }

        if (downOffsets.Count != CurveValidator.PointCount - 1)
        {
            throw new ArgumentException($"Expected {CurveValidator.PointCount - 1} down offsets, got {downOffsets.Count}.", nameof(downOffsets));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, CurveValidator.PointCount);
    }
}
