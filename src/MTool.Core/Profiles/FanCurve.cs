namespace MTool.Core.Profiles;

/// <summary>
/// One step of an EC fan table: above <see cref="UpThresholdC"/> the fan moves to
/// <see cref="SpeedPercent"/>; below <see cref="DownThresholdC"/> it drops back a step.
/// The first point has no thresholds (it is the idle speed).
/// </summary>
public sealed record FanPoint(int UpThresholdC, int DownThresholdC, int SpeedPercent);

public sealed record FanCurve(IReadOnlyList<FanPoint> Points);
