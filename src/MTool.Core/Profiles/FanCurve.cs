namespace MTool.Core.Profiles;

/// <summary>
/// One step of an EC fan table: above <see cref="UpThresholdC"/> the fan moves to
/// <see cref="SpeedPercent"/>; below <see cref="DownThresholdC"/> it drops back a step.
/// The first point has no thresholds (it is the idle speed).
/// </summary>
public sealed record FanPoint(int UpThresholdC, int DownThresholdC, int SpeedPercent);

public sealed record FanCurve(IReadOnlyList<FanPoint> Points)
{
    /// <summary>Builds a curve from (up, down, speed) steps.</summary>
    public static FanCurve Of(params (int Up, int Down, int Speed)[] steps) =>
        new(Array.AsReadOnly(steps.Select(s => new FanPoint(s.Up, s.Down, s.Speed)).ToArray()));

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
