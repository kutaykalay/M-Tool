using System.Collections.Frozen;
using MTool.Core.Device;

namespace MTool.Core.Ec;

/// <summary>A ceiling register that held a byte outside the layout; shown raw in the band and the log.</summary>
public sealed record Wmi2LayoutMismatch(byte Register, byte Value)
{
    public override string ToString() => $"0x{Register:X2}=0x{Value:X2}";
}

/// <summary>The once-per-session layout check; <see cref="Verified"/> only when nothing is out of place. Not value-comparable.</summary>
public sealed class Wmi2LayoutResult
{
    public Wmi2LayoutResult(IReadOnlyList<Wmi2LayoutMismatch> mismatches)
    {
        Mismatches = mismatches ?? throw new ArgumentNullException(nameof(mismatches));
    }

    public IReadOnlyList<Wmi2LayoutMismatch> Mismatches { get; }

    public bool Verified => Mismatches.Count == 0;

    public override string ToString() => Verified ? "verified" : string.Join(", ", Mismatches);
}

/// <summary>
/// Does this WMI2 laptop hold bytes a WMI2 laptop is known to hold (plan asama15 §4.4)? Checked once per
/// session before the first write: one register outside the layout closes every write of the session,
/// since the list may be wrong for this laptop. The sets are the same for every family and wider than what
/// may be written; the family's own bytes are <see cref="Wmi2WriteRules"/>' check. 0xD7 = 0x00 does not
/// fit: its meaning is unknown (Kutay, 2026-10-24).
/// </summary>
public static class Wmi2LayoutCheck
{
    // C5 is MSI Center's fourth mode (GhostDeck, plan §2a); read, never written.
    private static readonly FrozenSet<byte> PerformanceValues = FrozenSet.Create<byte>(0xC0, 0xC1, 0xC2, 0xC4, 0xC5, 0xC6);

    // 0x9D is the advanced variant seen next to 0x8D.
    private static readonly FrozenSet<byte> FanModeValues = FrozenSet.Create<byte>(0x0D, 0x1D, 0x8D, 0x9D);

    /// <summary>True when <paramref name="value"/> is a byte <paramref name="register"/> was seen to hold; false outside the ceiling.</summary>
    public static bool Fits(byte register, byte value) => register switch
    {
        // The low bits change by themselves; only bit 7 is Cooler Boost.
        Wmi2WriteCeiling.CoolerBoostRegister => true,
        Wmi2WriteCeiling.PerformanceRegister => PerformanceValues.Contains(value),
        Wmi2WriteCeiling.FanModeRegister => FanModeValues.Contains(value),
        // The status reading's own rule (enable bit, 10-100 %), so the two cannot drift.
        Wmi2WriteCeiling.ChargeLimitRegister => new Wmi2Status(0, 0, 0, value).ChargeLimitPercent is not null,
        _ => false,
    };

    public static Wmi2LayoutResult Check(Wmi2Status status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var mismatches = new List<Wmi2LayoutMismatch>();
        Add(mismatches, Wmi2WriteCeiling.CoolerBoostRegister, status.CoolerBoostRaw);
        Add(mismatches, Wmi2WriteCeiling.PerformanceRegister, status.PerformanceRaw);
        Add(mismatches, Wmi2WriteCeiling.FanModeRegister, status.FanModeRaw);
        Add(mismatches, Wmi2WriteCeiling.ChargeLimitRegister, status.ChargeRaw);
        return new Wmi2LayoutResult(mismatches.AsReadOnly());
    }

    private static void Add(List<Wmi2LayoutMismatch> mismatches, byte register, byte value)
    {
        if (!Fits(register, value))
        {
            mismatches.Add(new Wmi2LayoutMismatch(register, value));
        }
    }
}
