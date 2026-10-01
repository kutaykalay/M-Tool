using MTool.Core.Device;

namespace MTool.Core.Ec;

/// <summary>
/// Per-register whitelist and value rules. Whole-table rules live in
/// <see cref="Profiles.CurveValidator"/>.
/// </summary>
public static class EcWriteRules
{
    public const int MinUpThresholdC = 30;
    public const int MaxUpThresholdC = 95;
    public const int MaxSpeedPercent = 100;
    public const int MinChargeLimitPercent = 50;
    public const int MaxChargeLimitPercent = 100;

    private const byte ChargeLimitEnableBit = 0x80;
    private const byte CoolerBoostBit = 0x80;

    private static readonly FanRegisters[] Fans = [EcMap.CpuFan, EcMap.GpuFan];
    private static readonly HashSet<byte> PerformanceValues = [0xC0, 0xC1, 0xC2];
    private static readonly HashSet<byte> FanModeValues = [0x0D, 0x8D];

    private enum Kind { UpThreshold, Speed, CoolerBoost, ChargeLimit, Performance, FanMode }

    private static readonly IReadOnlyDictionary<byte, Kind> Kinds = BuildKinds();

    public static IReadOnlyCollection<byte> WritableRegisters { get; } = Kinds.Keys.Order().ToArray();

    public static bool IsFanTable(byte register) =>
        Kinds.TryGetValue(register, out var kind) && kind is Kind.UpThreshold or Kind.Speed;

    /// <summary>
    /// Safe write order: speeds, then up thresholds, other settings, fan mode last.
    /// A plan cut short mixes two valid tables instead of leaving a mode switch ahead of its table.
    /// </summary>
    public static int WriteOrder(byte register) => Kinds[register] switch
    {
        Kind.Speed => 0,
        Kind.UpThreshold => 1,
        Kind.FanMode => 3,
        _ => 2,
    };

    /// <summary>Rules that need no EC state. Returns an error message or null.</summary>
    public static string? CheckStatic(RegisterWrite write)
    {
        if (!Kinds.TryGetValue(write.Register, out var kind))
        {
            return $"0x{write.Register:X2} yazılabilir register listesinde değil.";
        }

        int value = write.Value;
        return kind switch
        {
            Kind.UpThreshold when value is < MinUpThresholdC or > MaxUpThresholdC =>
                $"{write}: yukarı eşik {MinUpThresholdC}-{MaxUpThresholdC} °C olmalı.",
            Kind.Speed when value > MaxSpeedPercent =>
                $"{write}: fan hızı en fazla %{MaxSpeedPercent}.",
            Kind.ChargeLimit when !IsValidChargeLimit(write.Value) =>
                $"{write}: şarj limiti 0x80 | %{MinChargeLimitPercent}-{MaxChargeLimitPercent} olmalı.",
            Kind.Performance when !PerformanceValues.Contains(write.Value) =>
                $"{write}: desteklenmeyen performans modu.",
            Kind.FanMode when !FanModeValues.Contains(write.Value) =>
                $"{write}: desteklenmeyen fan modu.",
            _ => null,
        };
    }

    /// <summary>Rules that compare against the register's current value. Returns an error message or null.</summary>
    public static string? CheckAgainstCurrent(RegisterWrite write, byte current) =>
        write.Register == EcMap.CoolerBoost && ((write.Value ^ current) & ~CoolerBoostBit & 0xFF) != 0
            ? $"{write}: Cooler Boost yazmasında yalnızca bit 7 değişebilir (şu an 0x{current:X2})."
            : null;

    private static bool IsValidChargeLimit(byte value)
    {
        var percent = value & ~ChargeLimitEnableBit;
        return (value & ChargeLimitEnableBit) != 0
            && percent is >= MinChargeLimitPercent and <= MaxChargeLimitPercent;
    }

    private static Dictionary<byte, Kind> BuildKinds()
    {
        var kinds = new Dictionary<byte, Kind>
        {
            [EcMap.CoolerBoost] = Kind.CoolerBoost,
            [EcMap.ChargeLimit] = Kind.ChargeLimit,
            [EcMap.PerformanceMode] = Kind.Performance,
            [EcMap.FanMode] = Kind.FanMode,
        };

        foreach (var fan in Fans)
        {
            AddRange(kinds, fan.UpThresholdsStart, EcMap.ThresholdCount, Kind.UpThreshold);
            AddRange(kinds, fan.SpeedsStart, EcMap.SpeedCount, Kind.Speed);
        }

        return kinds;
    }

    private static void AddRange(Dictionary<byte, Kind> kinds, byte start, int count, Kind kind)
    {
        for (var i = 0; i < count; i++)
        {
            kinds.Add((byte)(start + i), kind);
        }
    }
}
