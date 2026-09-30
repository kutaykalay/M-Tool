using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>
/// What the EC is set to right now. A null field means the register holds a value M-Tool does
/// not write (e.g. performance 0x80 after a reboot); <see cref="PerformanceRaw"/> keeps the byte.
/// </summary>
public sealed record ControlState(
    FanCurves FanCurves,
    PerformanceMode? Performance,
    byte PerformanceRaw,
    bool CoolerBoostOn,
    int? ChargeLimitPercent,
    FanMode? FanMode);
