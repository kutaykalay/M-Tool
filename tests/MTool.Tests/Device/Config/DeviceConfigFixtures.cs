using MTool.Core.Device.Config;
using MTool.Core.Profiles;

namespace MTool.Tests.Device.Config;

/// <summary>A valid P65 config built in code from <see cref="P65Golden"/>; tests break one field at a time.</summary>
internal static class DeviceConfigFixtures
{
    public static DeviceConfig P65() => new(
        SchemaVersion: 1,
        Id: "msi-p65-creator-9se",
        DisplayName: "MSI P65 Creator 9SE",
        Status: DeviceStatus.WriteVerified,
        Firmware: new FirmwareSpec([P65Golden.Firmware], ["16Q4EMS2.1"]),
        Interface: WmiInterface.Wmi1,
        FirmwareLocation: new FirmwareLocation(
            P65Golden.FirmwareVersion, P65Golden.FirmwareVersionLength, P65Golden.FirmwareDate, P65Golden.FirmwareDateLength),
        Fans: [CpuFan(), GpuFan()],
        Features: new FeatureSet(
            CoolerBoost: new CoolerBoostFeature(P65Golden.CoolerBoost, Bit: 7),
            ChargeLimit: new ChargeLimitFeature(P65Golden.ChargeLimit, EnableBit: 7, Min: 50, Max: 100),
            PerformanceMode: new PerformanceModeFeature(P65Golden.PerformanceMode,
                [new ModeValue("high", 0xC0), new ModeValue("balanced", 0xC1), new ModeValue("eco", 0xC2)]),
            FanMode: new FanModeFeature(P65Golden.FanMode, Auto: 0x0D, Advanced: 0x8D)),
        Wmi1: new Wmi1Layout([.. P65Golden.WmiFields.Select(f => new WmiFieldSpec(f.Key, f.Value.Class, f.Value.Index))]),
        PortRegisters: [.. P65Golden.PortRegisters],
        Limits: new CurveLimits(MinUpThresholdC: 30, MaxUpThresholdC: 95, MaxSpeedPercent: 100),
        Presets:
        [
            new PresetConfig("Cool", Curves(P65Golden.CoolCpuCurve, P65Golden.CoolGpuCurve)),
            new PresetConfig("Silent", Curves(P65Golden.SilentCpuCurve, P65Golden.SilentGpuCurve)),
        ],
        Sources: ["test fixture"]);

    /// <summary>A minimal read-only record: no firmware list, no offsets, no factory curve, no presets.</summary>
    public static DeviceConfig Draft() => P65() with
    {
        Id = "draft-test",
        Status = DeviceStatus.Draft,
        Firmware = new FirmwareSpec([], ["16Q4EMS9.1"]),
        Fans = [CpuFan() with { FactoryDownOffsets = null, FactoryCurve = null }],
        Presets = [],
    };

    public static FanConfig CpuFan() => new(
        Id: "cpu",
        Temperature: P65Golden.CpuFan.Temperature,
        SpeedPercent: P65Golden.CpuFan.SpeedPercent,
        RpmHigh: P65Golden.CpuFan.RpmHigh,
        UpThresholds: new RegisterBlock(P65Golden.CpuFan.UpThresholdsStart, 6),
        Speeds: new RegisterBlock(P65Golden.CpuFan.SpeedsStart, 7),
        FactoryDownOffsets: [.. P65Golden.CpuDownOffsets],
        FactoryCurve: Curve(P65Golden.FactoryCpuCurve));

    public static FanConfig GpuFan() => new(
        Id: "gpu",
        Temperature: P65Golden.GpuFan.Temperature,
        SpeedPercent: P65Golden.GpuFan.SpeedPercent,
        RpmHigh: P65Golden.GpuFan.RpmHigh,
        UpThresholds: new RegisterBlock(P65Golden.GpuFan.UpThresholdsStart, 6),
        Speeds: new RegisterBlock(P65Golden.GpuFan.SpeedsStart, 7),
        FactoryDownOffsets: [.. P65Golden.GpuDownOffsets],
        FactoryCurve: Curve(P65Golden.FactoryGpuCurve));

    public static FanCurve Curve(params (int Up, int Speed)[] points) => FanCurve.Of(points);

    public static IReadOnlyDictionary<string, FanCurve> Curves((int, int)[] cpu, (int, int)[] gpu) =>
        new Dictionary<string, FanCurve> { ["cpu"] = Curve(cpu), ["gpu"] = Curve(gpu) };
}
