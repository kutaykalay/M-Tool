using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>
/// What the EC is set to right now. A null mode means the register holds a value M-Tool does
/// not write (e.g. performance 0x80 after a reboot); <see cref="PerformanceRaw"/> keeps the byte.
/// Everything but <see cref="Port"/> is read through WMI.
/// </summary>
/// <param name="Port">Cooler Boost and charge limit, reachable only through the raw port: null when not known.</param>
public sealed record ControlState(
    FanCurves FanCurves,
    PerformanceMode? Performance,
    byte PerformanceRaw,
    FanMode? FanMode,
    PortState? Port);

/// <summary>The two registers only the raw port reaches (0x98, 0xEF), as raw bytes.</summary>
public sealed record PortState(byte CoolerBoostRaw, byte ChargeLimitRaw)
{
    public bool CoolerBoostOn => ModeCodes.IsCoolerBoostOn(CoolerBoostRaw);

    /// <summary>Null when the limit is off or holds a value M-Tool does not write.</summary>
    public int? ChargeLimitPercent => ModeCodes.ToChargeLimit(ChargeLimitRaw);
}
