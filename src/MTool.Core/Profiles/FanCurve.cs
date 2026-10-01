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
