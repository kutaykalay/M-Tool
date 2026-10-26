using MTool.Core.Device;
using MTool.Core.Device.Config;

namespace MTool.Core.Ec;

/// <summary>Why a WMI2 write was refused before it reached the EC.</summary>
public enum Wmi2Rejection
{
    /// <summary>The running firmware is not an exact entry of the list: a family match only reads.</summary>
    NotExactFirmware,

    /// <summary>The register is outside <see cref="Wmi2WriteCeiling"/>.</summary>
    NotWritable,

    /// <summary>The value is not one the ceiling and the firmware's family allow.</summary>
    ValueNotAllowed,

    /// <summary>The advanced fan mode turns on a curve M-Tool cannot read on WMI2.</summary>
    AdvancedFanMode,

    /// <summary>A Cooler Boost byte that changes more than bit 7.</summary>
    OtherBitsChanged,

    /// <summary>The register holds a byte outside the layout right now (<see cref="Wmi2LayoutCheck"/>).</summary>
    CurrentOutsideLayout,

    /// <summary>The firmware list holds an entry the code does not allow: it was never validated.</summary>
    ListInvalid,

    /// <summary>A write-path test that is not the current performance byte written again (K5).</summary>
    NotAWritePathTest,
}

/// <summary>A planned write, or why there is none; never both, never neither.</summary>
public sealed record Wmi2Planned
{
    private Wmi2Planned(RegisterWrite? write, Wmi2Rejection? rejection)
    {
        Write = write;
        Rejection = rejection;
    }

    public RegisterWrite? Write { get; }

    public Wmi2Rejection? Rejection { get; }

    public static Wmi2Planned Of(RegisterWrite write) => new(write ?? throw new ArgumentNullException(nameof(write)), null);

    public static Wmi2Planned Refused(Wmi2Rejection rejection) => new(null, rejection);
}

/// <summary>
/// The value rules of WMI2 experimental writes (plan asama15 §4.3-4.4), pure: the code's ceiling and the
/// firmware list's exact entries turn a <see cref="Wmi2Change"/> into one byte. <see cref="Check"/> is the
/// byte-level gate the gateway runs again right before <c>Set_Data</c>, so it does not trust the planner.
/// A list that breaks the code's rules (never validated) refuses every write instead of throwing.
/// </summary>
public static class Wmi2WriteRules
{
    private const byte WholeByte = 0xFF;
    private const byte CoolerBoostOtherBits = 0x7F;

    /// <summary>The bits a write sets and the read-back compares: bit 7 for Cooler Boost, the whole byte otherwise.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="register"/> is outside the ceiling.</exception>
    public static byte MaskOf(byte register) => register switch
    {
        Wmi2WriteCeiling.CoolerBoostRegister => Wmi2WriteCeiling.CoolerBoostMask,
        Wmi2WriteCeiling.PerformanceRegister or Wmi2WriteCeiling.FanModeRegister or Wmi2WriteCeiling.ChargeLimitRegister => WholeByte,
        _ => throw new ArgumentOutOfRangeException(nameof(register), register, "Not a WMI2 ceiling register."),
    };

    /// <summary>The write for <paramref name="change"/>, or why there is none.</summary>
    /// <param name="current">
    /// What <see cref="Wmi2Change.Register"/> holds now, read right before: the new byte keeps its other bits,
    /// and the low bits of 0x98 change by themselves, so an old read would write stale bits back.
    /// </param>
    public static Wmi2Planned Plan(Wmi2Change change, byte current, Wmi2FirmwareList list, FirmwareInfo? firmware)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(list);
        try
        {
            if (list.Match(firmware) != MatchKind.Exact)
            {
                return Wmi2Planned.Refused(Wmi2Rejection.NotExactFirmware);
            }

            if (ValueOf(change, current, list, firmware) is not { } value)
            {
                return Wmi2Planned.Refused(change is Wmi2Change.FanMode { Mode: Wmi2FanMode.Advanced }
                    ? Wmi2Rejection.AdvancedFanMode
                    : Wmi2Rejection.ValueNotAllowed);
            }

            var write = new RegisterWrite(change.Register, value);
            return Check(write, current, list, firmware) is { } refused ? Wmi2Planned.Refused(refused) : Wmi2Planned.Of(write);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Wmi2Planned.Refused(Wmi2Rejection.ListInvalid);
        }
    }

    /// <summary>Null when <paramref name="write"/> may go to the EC; otherwise the first rule it breaks.</summary>
    /// <param name="current">
    /// What the register holds now, read right before <c>Set_Data</c> (the same read the write was built from):
    /// it must fit the layout, and Cooler Boost keeps its other bits.
    /// </param>
    public static Wmi2Rejection? Check(RegisterWrite write, byte current, Wmi2FirmwareList list, FirmwareInfo? firmware)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(list);
        try
        {
            return CheckRules(write, current, list, firmware, restore: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Wmi2Rejection.ListInvalid;
        }
    }

    /// <summary>
    /// <see cref="Check"/> for writing back a byte the register held before (recovery, "back to before M-Tool"):
    /// the same rules, except that a charge limit may be 10-49 % too. Nothing else is widened.
    /// </summary>
    public static Wmi2Rejection? CheckRestore(RegisterWrite write, byte current, Wmi2FirmwareList list, FirmwareInfo? firmware)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(list);
        try
        {
            return CheckRules(write, current, list, firmware, restore: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Wmi2Rejection.ListInvalid;
        }
    }

    /// <summary>
    /// <see cref="Check"/>, and the write is the write-path test (K5): the current performance byte, a byte
    /// of the firmware's family, written again. Only this may run before the test has passed.
    /// </summary>
    public static Wmi2Rejection? CheckWritePathTest(RegisterWrite write, byte current, Wmi2FirmwareList list, FirmwareInfo? firmware)
    {
        if (Check(write, current, list, firmware) is { } rejection)
        {
            return rejection;
        }

        return write.Register == Wmi2WriteCeiling.PerformanceRegister && write.Value == current ? null : Wmi2Rejection.NotAWritePathTest;
    }

    private static Wmi2Rejection? CheckRules(RegisterWrite write, byte current, Wmi2FirmwareList list, FirmwareInfo? firmware, bool restore)
    {
        if (list.Match(firmware) != MatchKind.Exact)
        {
            return Wmi2Rejection.NotExactFirmware;
        }

        if (!Wmi2WriteCeiling.Registers.Contains(write.Register))
        {
            return Wmi2Rejection.NotWritable;
        }

        if (!Wmi2LayoutCheck.Fits(write.Register, current))
        {
            return Wmi2Rejection.CurrentOutsideLayout;
        }

        return write.Register switch
        {
            Wmi2WriteCeiling.CoolerBoostRegister when ((write.Value ^ current) & CoolerBoostOtherBits) != 0 => Wmi2Rejection.OtherBitsChanged,
            Wmi2WriteCeiling.PerformanceRegister when !IsFamilyPerformanceByte(write.Value, list, firmware) => Wmi2Rejection.ValueNotAllowed,
            Wmi2WriteCeiling.FanModeRegister when !Wmi2WriteCeiling.FanModeValues.Values.Contains(write.Value) => Wmi2Rejection.ValueNotAllowed,
            Wmi2WriteCeiling.ChargeLimitRegister when !(restore
                ? Wmi2WriteCeiling.IsRestorableChargeLimitByte(write.Value)
                : Wmi2WriteCeiling.IsChargeLimitByte(write.Value)) => Wmi2Rejection.ValueNotAllowed,
            _ => null,
        };
    }

    /// <summary>The byte for <paramref name="change"/>; null for a value the ceiling has no byte for.</summary>
    private static byte? ValueOf(Wmi2Change change, byte current, Wmi2FirmwareList list, FirmwareInfo? firmware) => change switch
    {
        Wmi2Change.CoolerBoost { On: true } => (byte)(current | Wmi2WriteCeiling.CoolerBoostMask),
        Wmi2Change.CoolerBoost => (byte)(current & CoolerBoostOtherBits),
        Wmi2Change.Performance { Mode: var mode } when Wmi2WriteCeiling.StandardPerformance.ContainsKey(mode) => list.PerformanceByte(firmware, mode),
        Wmi2Change.FanMode { Mode: var mode } when Wmi2WriteCeiling.FanModeValues.TryGetValue(mode, out var value) => value,
        Wmi2Change.ChargeLimit { Percent: >= Wmi2WriteCeiling.MinChargeLimitPercent and <= Wmi2WriteCeiling.MaxChargeLimitPercent } charge =>
            (byte)(Wmi2WriteCeiling.ChargeLimitEnableMask | charge.Percent),
        _ => null,
    };

    /// <summary>
    /// The byte the firmware's family uses for one of the modes; C0 and C6 only where its override says so.
    /// Every mode is resolved first, so a bad override refuses the register even for another mode's byte.
    /// </summary>
    private static bool IsFamilyPerformanceByte(byte value, Wmi2FirmwareList list, FirmwareInfo? firmware) =>
        Wmi2WriteCeiling.StandardPerformance.Keys.Select(mode => list.PerformanceByte(firmware, mode)).ToArray().Contains(value);
}
