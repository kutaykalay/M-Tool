namespace MTool.Core.Ec;

/// <summary>
/// One <c>Set_Data</c> call: an EC address and the byte for it. Made only through <see cref="Wmi2Writes.Request"/>,
/// and the constructor is private, so not even code in this assembly can make one outside <see cref="Wmi2WriteCeiling"/>.
/// </summary>
public sealed record Wmi2WriteRequest
{
    private Wmi2WriteRequest(byte register, byte value) => (Register, Value) = (register, value);

    public byte Register { get; }

    public byte Value { get; }

    /// <exception cref="InvalidOperationException">The register or the byte is outside the ceiling.</exception>
    internal static Wmi2WriteRequest Create(byte register, byte value) =>
        Wmi2Writes.IsWithinCeiling(register, value)
            ? new Wmi2WriteRequest(register, value)
            : throw new InvalidOperationException($"{Wmi2Writes.SetData}(0x{register:X2}, 0x{value:X2}) is outside the WMI2 write ceiling.");
}

/// <summary>
/// The type gate of WMI2 writes (CLAUDE.md, "WMI2 deneysel yazma"): a request only for a ceiling register
/// and a byte the ceiling has for it, with no firmware list and no current value involved. It is the last
/// line, not the rules: <see cref="Wmi2WriteRules.Check"/> decides first and lets through less than this.
/// </summary>
public static class Wmi2Writes
{
    public const string SetData = "Set_Data";

    /// <summary>The request for <paramref name="register"/> = <paramref name="value"/>.</summary>
    /// <exception cref="InvalidOperationException">The register or the byte is outside the ceiling.</exception>
    public static Wmi2WriteRequest Request(byte register, byte value) => Wmi2WriteRequest.Create(register, value);

    /// <summary>
    /// True for a byte the ceiling has for the register, whatever the family: any Cooler Boost byte (the rules
    /// keep its other bits), a standard or exception performance byte, auto or silent, a 50-100 % charge limit.
    /// </summary>
    public static bool IsWithinCeiling(byte register, byte value) => register switch
    {
        Wmi2WriteCeiling.CoolerBoostRegister => true,
        Wmi2WriteCeiling.PerformanceRegister => Wmi2WriteCeiling.StandardPerformance.Values.Contains(value)
            || Wmi2WriteCeiling.PerformanceExceptions.Values.Contains(value),
        Wmi2WriteCeiling.FanModeRegister => Wmi2WriteCeiling.FanModeValues.Values.Contains(value),
        Wmi2WriteCeiling.ChargeLimitRegister => Wmi2WriteCeiling.IsChargeLimitByte(value),
        _ => false,
    };
}
