using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>
/// What the EC is set to right now. A null mode means the register holds a value M-Tool does
/// not write (e.g. performance 0x80 after a reboot); <see cref="PerformanceRaw"/> keeps the byte.
/// Everything but <see cref="Port"/> is read through WMI.
/// </summary>
/// <param name="Port">Cooler Boost and charge limit, reachable only through the raw port: null when not known.</param>
/// <param name="KeyboardBacklight">Null when the model has none.</param>
public sealed record ControlState(
    FanCurves FanCurves,
    PerformanceMode? Performance,
    byte PerformanceRaw,
    FanMode? FanMode,
    PortState? Port,
    KeyboardBacklightState? KeyboardBacklight = null);

/// <summary>The keyboard backlight byte and its step (0 = off); a null step means a byte the record does not list.</summary>
public sealed record KeyboardBacklightState(int? Level, byte Raw);

/// <summary>The two registers only the raw port reaches (0x98, 0xEF), as raw bytes.</summary>
/// <param name="CoolerBoostKnown">False after a wake: the EC turns Cooler Boost off in sleep, and the port is not read then.</param>
public sealed record PortState(byte CoolerBoostRaw, byte ChargeLimitRaw, bool CoolerBoostKnown = true)
{
    /// <summary>Null while <see cref="CoolerBoostKnown"/> is false.</summary>
    public bool? CoolerBoostOn => CoolerBoostKnown ? ModeCodes.IsCoolerBoostOn(CoolerBoostRaw) : null;

    /// <summary>Null when the limit is off or holds a value M-Tool does not write.</summary>
    public int? ChargeLimitPercent => ModeCodes.ToChargeLimit(ChargeLimitRaw);
}
