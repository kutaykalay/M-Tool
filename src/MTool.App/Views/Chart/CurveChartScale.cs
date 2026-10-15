using System.Windows;
using System.Windows.Input;
using MTool.App.ViewModels;
using MTool.Core.Profiles;

namespace MTool.App.Views.Chart;

/// <summary>
/// The fan curve chart's geometry: degrees and percent to pixels and back, and where each part is
/// drawn. Pure, so the drag and keyboard maths is tested without a window. It never enforces the
/// curve rules; the editor clamps every move.
/// </summary>
internal static class CurveChartScale
{
    /// <summary>
    /// Left edge of the temperature axis. Below the lowest threshold: the first step drops back by its
    /// factory down offset, so its down marker sits left of it (a test ties this to the rules).
    /// </summary>
    public const int MinC = 20;

    /// <summary>Right edge of the temperature axis, at or above the highest threshold.</summary>
    public const int MaxC = 100;

    private const int MinPct = 0;
    private const int MaxPct = 100;
    private const int XTickStepC = 10;
    private const int YTickStepPct = 20;
    private const int SmallStep = 1;
    private const int LargeStep = 5;

    /// <summary>Temperatures with a grid line and a label.</summary>
    public static IReadOnlyList<int> XTicks { get; } = Ticks(MinC, MaxC, XTickStepC);

    /// <summary>Speeds with a grid line and a label.</summary>
    public static IReadOnlyList<int> YTicks { get; } = Ticks(MinPct, MaxPct, YTickStepPct);

    /// <summary>Where a temperature and speed are drawn; speed grows upwards.</summary>
    public static Point ToPixel(double c, double pct, Rect plot) => new(
        plot.Left + ((c - MinC) * plot.Width / (MaxC - MinC)),
        plot.Bottom - ((pct - MinPct) * plot.Height / (MaxPct - MinPct)));

    /// <summary>
    /// The nearest whole value under a pixel; a pixel off the plot lands on its edge. Null for an
    /// empty plot or a pixel that is not a number (a layout bug, never a corner value).
    /// </summary>
    public static (int C, int Pct)? ToValue(Point pixel, Rect plot)
    {
        if (!(plot.Width > 0 && plot.Height > 0 && double.IsFinite(plot.Width) && double.IsFinite(plot.Height))
            || double.IsNaN(pixel.X) || double.IsNaN(pixel.Y))
        {
            return null;
        }

        // Multiplying first keeps a pixel half way between two values exactly at .5.
        var c = MinC + ((pixel.X - plot.Left) * (MaxC - MinC) / plot.Width);
        var pct = MinPct + ((plot.Bottom - pixel.Y) * (MaxPct - MinPct) / plot.Height);
        return (RoundToAxis(c, MinC, MaxC), RoundToAxis(pct, MinPct, MaxPct));
    }

    /// <summary>Where a point's handle sits: the idle point has no threshold, so it sits on the left edge.</summary>
    public static (double C, double Pct) PointPosition(PointViewModel point) =>
        (point.IsIdle ? MinC : ClampC(point.UpC), ClampPct(point.SpeedPercent));

    /// <summary>
    /// The curve as the EC runs it: above a threshold the fan jumps to the next speed, so each point
    /// is a corner of a step, from the left edge to the right edge.
    /// </summary>
    public static IReadOnlyList<(double C, double Pct)> StepVertices(FanCurve curve)
    {
        var points = curve.Points;
        if (points.Count == 0)
        {
            return [];
        }

        var vertices = new List<(double C, double Pct)> { (MinC, ClampPct(points[0].SpeedPercent)) };
        for (var i = 1; i < points.Count; i++)
        {
            var c = ClampC(points[i].UpThresholdC);
            vertices.Add((c, ClampPct(points[i - 1].SpeedPercent)));
            vertices.Add((c, ClampPct(points[i].SpeedPercent)));
        }

        vertices.Add((MaxC, ClampPct(points[^1].SpeedPercent)));
        return vertices;
    }

    /// <summary>
    /// One dashed vertical line per step after the idle point: below its down threshold
    /// <c>C</c> the fan drops back from the step's speed <c>FromPct</c> to the one below, <c>ToPct</c>.
    /// </summary>
    /// <param name="downThresholdsC">From <see cref="FanCurve.DownThresholdsC"/>, one per step.</param>
    /// <exception cref="ArgumentException">The thresholds do not match the curve's steps.</exception>
    public static IReadOnlyList<(double C, double FromPct, double ToPct)> DownMarkers(
        FanCurve curve, IReadOnlyList<int> downThresholdsC)
    {
        var points = curve.Points;
        if (points.Count == 0 && downThresholdsC.Count == 0)
        {
            return [];
        }

        if (downThresholdsC.Count != points.Count - 1)
        {
            throw new ArgumentException(
                $"Expected {points.Count - 1} down thresholds, got {downThresholdsC.Count}.", nameof(downThresholdsC));
        }

        return [.. downThresholdsC.Select((down, i) =>
            (ClampC(down), ClampPct(points[i + 1].SpeedPercent), ClampPct(points[i].SpeedPercent)))];
    }

    /// <summary>
    /// The box a point may be dragged in; a line on the left edge for the idle point, whose threshold
    /// never moves. Null when either range is empty: the curve is already invalid there, and the
    /// error band explains it better than a half box.
    /// </summary>
    public static (double MinC, double MaxC, double MinPct, double MaxPct)? LimitsArea(PointViewModel point)
    {
        var limits = point.Limits;
        if (limits.MinSpeedPercent > limits.MaxSpeedPercent || (!point.IsIdle && limits.MinUpC > limits.MaxUpC))
        {
            return null;
        }

        var (minC, maxC) = point.IsIdle ? (MinC, MinC) : (ClampC(limits.MinUpC), ClampC(limits.MaxUpC));
        return (minC, maxC, ClampPct(limits.MinSpeedPercent), ClampPct(limits.MaxSpeedPercent));
    }

    /// <summary>
    /// Where an arrow key asks a point to go: left/right change the threshold, up/down the speed,
    /// by 5 with Shift. Null for any other key. Past a limit is still asked for; the editor clamps it.
    /// </summary>
    public static (int UpC, int Pct)? KeyStep(PointViewModel point, Key key, bool shift)
    {
        var step = shift ? LargeStep : SmallStep;
        var thresholdStep = point.IsIdle ? 0 : step;
        return key switch
        {
            Key.Left => (point.UpC - thresholdStep, point.SpeedPercent),
            Key.Right => (point.UpC + thresholdStep, point.SpeedPercent),
            Key.Up => (point.UpC, point.SpeedPercent + step),
            Key.Down => (point.UpC, point.SpeedPercent - step),
            _ => null,
        };
    }

    // An infinite value saturates in the cast and is then clamped to the edge.
    private static int RoundToAxis(double value, int min, int max) =>
        Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), min, max);

    private static double ClampC(int c) => Math.Clamp(c, MinC, MaxC);

    private static double ClampPct(int pct) => Math.Clamp(pct, MinPct, MaxPct);

    private static IReadOnlyList<int> Ticks(int from, int to, int step) =>
        Array.AsReadOnly([.. Enumerable.Range(0, ((to - from) / step) + 1).Select(i => from + (i * step))]);
}
