using MTool.Core.Ec;

namespace MTool.Core.Device;

public enum Wmi2FanMode
{
    Auto,
    Silent,
    Advanced,
}

/// <summary>
/// The status registers of a WMI2 model, raw, with the names the tested models agree on. Every
/// name is unverified on M-Tool's side; the raw byte is always shown with it.
/// </summary>
public sealed record Wmi2Status(byte CoolerBoostRaw, byte PerformanceRaw, byte FanModeRaw, byte ChargeRaw)
{
    private const byte EnableBit = 0x80;
    private const byte PercentMask = 0x7F;
    private const int MinChargePercent = 10;
    private const int MaxChargePercent = 100;

    public bool CoolerBoost => (CoolerBoostRaw & EnableBit) != 0;

    /// <summary>C1 balanced (also MSI Center's silent), C2 eco, C4 extreme; null for anything else.</summary>
    public PerformanceMode? Performance => PerformanceRaw switch
    {
        0xC1 => PerformanceMode.Balanced,
        0xC2 => PerformanceMode.Eco,
        0xC4 => PerformanceMode.High,
        _ => null,
    };

    /// <summary>Bit 7 marks a custom curve (0x8D, also seen as 0x9D); 0x0D auto, 0x1D silent.</summary>
    public Wmi2FanMode? FanMode => (FanModeRaw & EnableBit) != 0
        ? Wmi2FanMode.Advanced
        : FanModeRaw switch
        {
            0x0D => Wmi2FanMode.Auto,
            0x1D => Wmi2FanMode.Silent,
            _ => null,
        };

    /// <summary>0x80 | percent; null when the limit is off or the value is out of range.</summary>
    public int? ChargeLimitPercent =>
        (ChargeRaw & EnableBit) != 0 && (ChargeRaw & PercentMask) is var percent and >= MinChargePercent and <= MaxChargePercent
            ? percent
            : null;
}

/// <summary>
/// The register map of WMI2 laptops (B family; GhostDeck's G2): the same on all 48 models GhostDeck
/// tested, from msi-ec, YAMDCC and GhostDeck (docs/plans/asama10.md §1.6). Read-only, unverified.
/// </summary>
public static class Wmi2Family
{
    public const string Sources =
        "GhostDeck 7f3a670 (issues #1, #6, #14, #47, #52, #90, #180, #228), msi-ec, YAMDCC";

    public const byte CpuTemperature = 0x68;
    public const byte GpuTemperature = 0x80;
    public const byte CpuFanPercent = 0x71;
    public const byte GpuFanPercent = 0x89;
    public const byte CpuTach = 0xC8;
    public const byte GpuTach = 0xCA;
    public const int TachLength = 2;

    private const byte CoolerBoostRegister = 0x98;
    private const byte PerformanceModeRegister = 0xD2;
    private const byte FanModeRegister = 0xD4;
    private const byte ChargeLimitRegister = 0xD7;

    public static IReadOnlyList<byte> SensorRegisters { get; } = Array.AsReadOnly<byte>(
        [CpuTemperature, GpuTemperature, CpuFanPercent, GpuFanPercent, CpuTach, CpuTach + 1, GpuTach, GpuTach + 1]);

    public static IReadOnlyList<byte> StatusRegisters { get; } = Array.AsReadOnly<byte>(
        [CoolerBoostRegister, PerformanceModeRegister, FanModeRegister, ChargeLimitRegister]);

    /// <summary>An unclear tach reads as 0 RPM; <see cref="SensorSnapshot.Tach"/> keeps the raw bytes.</summary>
    public static SensorSnapshot ReadSensors(IEcRegisters ec)
    {
        var cpuTemp = SensorRead.Temperature(ec, CpuTemperature);
        var gpuTemp = SensorRead.Temperature(ec, GpuTemperature);
        var cpuPercent = ec.Read(CpuFanPercent);
        var gpuPercent = ec.Read(GpuFanPercent);
        var cpu = ec.ReadBlock(CpuTach, TachLength);
        var gpu = ec.ReadBlock(GpuTach, TachLength);
        var tach = new Wmi2Tach(cpu[0], cpu[1], gpu[0], gpu[1]);
        return new SensorSnapshot(
            cpuTemp,
            gpuTemp,
            cpuPercent,
            gpuPercent,
            Wmi2Rpm.Resolve(tach.CpuHigh, tach.CpuLow) ?? 0,
            Wmi2Rpm.Resolve(tach.GpuHigh, tach.GpuLow) ?? 0,
            tach);
    }

    public static Wmi2Status ReadStatus(IEcRegisters ec) =>
        new(ec.Read(CoolerBoostRegister), ec.Read(PerformanceModeRegister), ec.Read(FanModeRegister), ec.Read(ChargeLimitRegister));
}
