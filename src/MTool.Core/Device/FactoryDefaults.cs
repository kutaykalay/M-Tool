using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>
/// Factory fan tables of firmware 16Q4EMS2.107, embedded instead of trusting a first-run read
/// (plan.md §4.3). Read by YAMDCC from the EC and matched byte for byte in Faz 0.
/// </summary>
public static class FactoryDefaults
{
    public static FanCurves FanCurves { get; } = new(
        Cpu: FanCurve.Of((0, 0, 45), (55, 47, 50), (64, 61, 60), (70, 67, 70), (76, 73, 75), (82, 79, 80), (88, 85, 80)),
        Gpu: FanCurve.Of((0, 0, 0), (55, 47, 50), (61, 58, 60), (65, 62, 70), (71, 68, 80), (77, 74, 90), (86, 81, 90)));

    public static FanProfile Profile { get; } = new("Default", FanCurves);
}
