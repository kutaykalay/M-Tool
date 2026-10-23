using System.Collections.Frozen;
using MTool.Core.Device;

namespace MTool.Core.Ec;

/// <summary>
/// Everything a WMI2 experimental write may ever touch, fixed in code (CLAUDE.md, "WMI2 deneysel yazma"):
/// Cooler Boost, performance mode, fan mode (auto/silent) and charge limit. A firmware list can only
/// choose, per family, the <see cref="PerformanceExceptions"/> byte of a mode instead of its standard
/// byte; it can never add a register, a value or another meaning. Super Battery (0xEB) is deferred.
/// </summary>
public static class Wmi2WriteCeiling
{
    public const byte CoolerBoostRegister = 0x98;
    public const byte CoolerBoostMask = 0x80;
    public const byte PerformanceRegister = 0xD2;
    public const byte FanModeRegister = 0xD4;
    public const byte ChargeLimitRegister = 0xD7;
    public const byte ChargeLimitEnableMask = 0x80;

    // Own literals, not EcWriteRules': a change to the P65 range must not move the WMI2 ceiling.
    public const int MinChargeLimitPercent = 50;
    public const int MaxChargeLimitPercent = 100;

    public static IReadOnlyList<byte> Registers { get; } = Array.AsReadOnly<byte>(
        [CoolerBoostRegister, PerformanceRegister, FanModeRegister, ChargeLimitRegister]);

    /// <summary>The bytes msi-ec's G2 configs and GhostDeck's tested models agree on.</summary>
    public static IReadOnlyDictionary<PerformanceMode, byte> StandardPerformance { get; } = new Dictionary<PerformanceMode, byte>
    {
        [PerformanceMode.Balanced] = 0xC1,
        [PerformanceMode.Eco] = 0xC2,
        [PerformanceMode.High] = 0xC4,
    }.ToFrozenDictionary();

    /// <summary>
    /// The one vendor byte each mode may use instead of the standard one, seen on single families:
    /// eco C6 (15M1IMS2, GhostDeck #104, #234) and high C0 (14D3EMS1, GhostDeck #170). Balanced has none.
    /// </summary>
    public static IReadOnlyDictionary<PerformanceMode, byte> PerformanceExceptions { get; } = new Dictionary<PerformanceMode, byte>
    {
        [PerformanceMode.Eco] = 0xC6,
        [PerformanceMode.High] = 0xC0,
    }.ToFrozenDictionary();

    /// <summary>Advanced (0x8D, a custom curve) is never written: there is no fan table write on WMI2.</summary>
    public static IReadOnlyDictionary<Wmi2FanMode, byte> FanModeValues { get; } = new Dictionary<Wmi2FanMode, byte>
    {
        [Wmi2FanMode.Auto] = 0x0D,
        [Wmi2FanMode.Silent] = 0x1D,
    }.ToFrozenDictionary();

    /// <summary>True when <paramref name="value"/> is the exception byte the code allows for <paramref name="mode"/>.</summary>
    public static bool IsPerformanceException(PerformanceMode mode, byte value) =>
        PerformanceExceptions.TryGetValue(mode, out var allowed) && allowed == value;
}
