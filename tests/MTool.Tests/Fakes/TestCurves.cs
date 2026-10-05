using MTool.Core.Profiles;

namespace MTool.Tests.Fakes;

/// <summary>Valid custom fan curves for tests: Silent with a different CPU curve.</summary>
internal static class TestCurves
{
    public static readonly FanCurves Night = Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    };

    public static readonly FanCurves QuieterNight = Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 25), (60, 40), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    };
}
