using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>Builds <see cref="WritePlan"/>s for the P65. Pure: validation happens in <see cref="EcGateway"/>.</summary>
public static class WritePlans
{
    public static WritePlan FanCurves(FanCurves curves, string profileName)
    {
        var writes = TableWrites(EcMap.CpuFan, curves.Cpu).Concat(TableWrites(EcMap.GpuFan, curves.Gpu)).ToArray();
        return new WritePlan($"Fan profile: {profileName}", Array.AsReadOnly(writes));
    }

    public static WritePlan CoolerBoost(bool on, byte currentValue)
    {
        var value = on ? currentValue | ModeCodes.CoolerBoostBit : currentValue & ~ModeCodes.CoolerBoostBit;
        return Single($"Cooler Boost: {(on ? "on" : "off")}", EcMap.CoolerBoost, (byte)value);
    }

    public static WritePlan ChargeLimit(int percent) =>
        Single($"Charge limit: {percent}%", EcMap.ChargeLimit, ModeCodes.ChargeLimitByte(percent));

    public static WritePlan Performance(PerformanceMode mode) =>
        Single($"Performance mode: {ModeNames.Of(mode)}", EcMap.PerformanceMode, ModeCodes.PerformanceByte(mode));

    public static WritePlan Fan(FanMode mode) =>
        Single($"Fan mode: {ModeNames.Of(mode)}", EcMap.FanMode, ModeCodes.FanModeByte(mode));

    private static WritePlan Single(string description, byte register, byte value) =>
        new(description, [new RegisterWrite(register, value)]);

    private static IEnumerable<RegisterWrite> TableWrites(FanRegisters fan, FanCurve curve)
    {
        var tables = FanTableCodec.Encode(curve);
        return Block(fan.UpThresholdsStart, tables.UpThresholds)
            .Concat(Block(fan.SpeedsStart, tables.Speeds));
    }

    private static IEnumerable<RegisterWrite> Block(byte start, IReadOnlyList<byte> values) =>
        values.Select((value, i) => new RegisterWrite((byte)(start + i), value));
}
