using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

public enum PerformanceMode
{
    High,
    Balanced,
    Eco,
}

public enum FanMode
{
    Auto,
    Advanced,
}

/// <summary>Builds <see cref="WritePlan"/>s for the P65. Pure: validation happens in <see cref="EcGateway"/>.</summary>
public static class WritePlans
{
    private const byte CoolerBoostBit = 0x80;
    private const byte ChargeLimitEnableBit = 0x80;

    public static WritePlan FanCurves(FanCurves curves, string profileName)
    {
        var writes = TableWrites(EcMap.CpuFan, curves.Cpu).Concat(TableWrites(EcMap.GpuFan, curves.Gpu)).ToArray();
        return new WritePlan($"Fan profili: {profileName}", Array.AsReadOnly(writes));
    }

    public static WritePlan CoolerBoost(bool on, byte currentValue)
    {
        var value = on ? currentValue | CoolerBoostBit : currentValue & ~CoolerBoostBit;
        return Single($"Cooler Boost: {(on ? "açık" : "kapalı")}", EcMap.CoolerBoost, (byte)value);
    }

    public static WritePlan ChargeLimit(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, EcWriteRules.MinChargeLimitPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, EcWriteRules.MaxChargeLimitPercent);
        return Single($"Şarj limiti: %{percent}", EcMap.ChargeLimit, (byte)(ChargeLimitEnableBit | percent));
    }

    public static WritePlan Performance(PerformanceMode mode) => Single(
        $"Performans modu: {mode}",
        EcMap.PerformanceMode,
        mode switch
        {
            PerformanceMode.High => 0xC0,
            PerformanceMode.Balanced => 0xC1,
            PerformanceMode.Eco => 0xC2,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        });

    public static WritePlan Fan(FanMode mode) => Single(
        $"Fan modu: {mode}",
        EcMap.FanMode,
        mode switch
        {
            FanMode.Auto => 0x0D,
            FanMode.Advanced => 0x8D,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        });

    private static WritePlan Single(string description, byte register, byte value) =>
        new(description, [new RegisterWrite(register, value)]);

    private static IEnumerable<RegisterWrite> TableWrites(FanRegisters fan, FanCurve curve)
    {
        var tables = FanTableCodec.Encode(curve);
        return Block(fan.UpThresholdsStart, tables.UpThresholds)
            .Concat(Block(fan.SpeedsStart, tables.Speeds))
            .Concat(Block(fan.DownOffsetsStart, tables.DownOffsets));
    }

    private static IEnumerable<RegisterWrite> Block(byte start, IReadOnlyList<byte> values) =>
        values.Select((value, i) => new RegisterWrite((byte)(start + i), value));
}
