using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>
/// Factory fan tables of firmware 16Q4EMS2.107, embedded instead of trusting a first-run read:
/// another tool may have changed them by then. Read by YAMDCC from the EC and matched byte for byte
/// against a dump of this laptop.
/// </summary>
public static class FactoryDefaults
{
    public static FanCurves FanCurves { get; } = new(
        Cpu: FanCurve.Of((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80)),
        Gpu: FanCurve.Of((0, 0), (55, 50), (61, 60), (65, 70), (71, 80), (77, 90), (86, 90)));

    public static FanProfile Profile { get; } = new("Default", FanCurves);

    /// <summary>
    /// Factory down offsets (0x7A-0x7F, 0x92-0x97). Every curve is validated against them without
    /// reading them back: M-Tool never writes them, WMI cannot reach them, and the EC restores them on
    /// every reboot (2026-09-30 dump), so a change made by another tool lasts until the next restart.
    /// </summary>
    public static IReadOnlyList<int> CpuDownOffsets { get; } = Array.AsReadOnly([8, 3, 3, 3, 3, 3]);

    public static IReadOnlyList<int> GpuDownOffsets { get; } = Array.AsReadOnly([8, 3, 3, 3, 3, 5]);
}
