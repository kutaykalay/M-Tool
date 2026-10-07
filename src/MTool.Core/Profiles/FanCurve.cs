namespace MTool.Core.Profiles;

/// <summary>
/// One step of an EC fan table: above <see cref="UpThresholdC"/> the fan moves to
/// <see cref="SpeedPercent"/>. It drops back a step below the up threshold minus the EC's factory
/// down offset, which M-Tool never writes (WMI has no access to it). The first point has no
/// threshold (it is the idle speed).
/// </summary>
public sealed record FanPoint(int UpThresholdC, int SpeedPercent);

public sealed record FanCurve(IReadOnlyList<FanPoint> Points)
{
    private const int TablePoints = 7;
    private const int MinPlausibleThresholdC = 20;
    private const int MaxPlausibleThresholdC = 110;
    private const int MaxPlausibleSpeedPercent = 150;

    /// <summary>
    /// Whether a table read from the EC can be a fan table at all: seven points, the six thresholds
    /// after the idle point rising within 20-110 °C, every speed 0-150 % (YAMDCC's ceiling). Not a safety check for writes
    /// (<c>CurveValidator</c> is): it tells nonsense read with an unverified map from a real table.
    /// </summary>
    public bool IsPlausible()
    {
        if (Points.Count != TablePoints || Points.Any(p => p.SpeedPercent is < 0 or > MaxPlausibleSpeedPercent))
        {
            return false;
        }

        var thresholds = Points.Skip(1).Select(p => p.UpThresholdC).ToArray();
        return thresholds.All(t => t is >= MinPlausibleThresholdC and <= MaxPlausibleThresholdC)
            && thresholds.Zip(thresholds.Skip(1)).All(pair => pair.First < pair.Second);
    }

    /// <summary>Builds a curve from (up, speed) steps.</summary>
    public static FanCurve Of(params (int Up, int Speed)[] steps) =>
        new(Array.AsReadOnly(steps.Select(s => new FanPoint(s.Up, s.Speed)).ToArray()));

    /// <summary>Where each step after the idle point drops back: its up threshold minus the matching down offset.</summary>
    public IReadOnlyList<int> DownThresholdsC(IReadOnlyList<int> downOffsets)
    {
        if (downOffsets.Count != Points.Count - 1)
        {
            throw new ArgumentException($"Expected {Points.Count - 1} down offsets, got {downOffsets.Count}.", nameof(downOffsets));
        }

        return Array.AsReadOnly(Points.Skip(1).Select((p, i) => p.UpThresholdC - downOffsets[i]).ToArray());
    }

    /// <summary>By points, not by list reference: a table read from the EC equals the profile it came from.</summary>
    public bool Equals(FanCurve? other) => other is not null && Points.SequenceEqual(other.Points);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in Points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }
}

public sealed record FanCurves(FanCurve Cpu, FanCurve Gpu);

public sealed record FanProfile(string Name, FanCurves Curves);
