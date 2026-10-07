using MTool.Core.Device.Config;

namespace MTool.Core.Device;

/// <summary>
/// The registers M-Tool reads on one model, taken from its validated <see cref="DeviceConfig"/>.
/// The read side (router, device reader, dump) uses this; the write side keeps the P65's verified
/// statics (<see cref="EcMap"/>, <see cref="WmiMap"/>, <see cref="Ec.EcWriteRules"/>). For now a
/// layout needs two fans, a WMI1 map and all four features; other models come later.
/// </summary>
public sealed class DeviceLayout
{
    private const string CpuFanId = "cpu";
    private const string GpuFanId = "gpu";

    private DeviceLayout(DeviceConfig config, FanRegisters cpu, FanRegisters gpu, FeatureSet features, Wmi1Layout wmi)
    {
        Id = config.Id;
        Firmware = config.FirmwareLocation;
        CpuFan = cpu;
        GpuFan = gpu;
        CoolerBoost = features.CoolerBoost!.Register;
        ChargeLimit = features.ChargeLimit!.Register;
        PerformanceMode = features.PerformanceMode!.Register;
        FanMode = features.FanMode!.Register;
        Wmi = WmiFieldMap.From(config, wmi);
        Capabilities = DeviceCapabilities.From(config);
        WatchedRegisters = Array.AsReadOnly(
            [.. new[] { cpu, gpu }.SelectMany(TableRegisters).Append(PerformanceMode).Append(FanMode).Order()]);
    }

    /// <summary>The record this layout came from, for the log.</summary>
    public string Id { get; }

    public FirmwareLocation Firmware { get; }

    public FanRegisters CpuFan { get; }

    public FanRegisters GpuFan { get; }

    public byte CoolerBoost { get; }

    public byte ChargeLimit { get; }

    public byte PerformanceMode { get; }

    public byte FanMode { get; }

    public WmiFieldMap Wmi { get; }

    /// <summary>The controls the record has, for the UI.</summary>
    public DeviceCapabilities Capabilities { get; }

    /// <summary>Both fan tables, performance mode and fan mode, in register order: what a watch compares.</summary>
    public IReadOnlyList<byte> WatchedRegisters { get; }

    /// <exception cref="ArgumentException">
    /// The record is invalid or has no layout yet (one fan, no WMI1, a missing feature). The message is
    /// meant for people, so it carries no parameter name.
    /// </exception>
    public static DeviceLayout From(DeviceConfig config)
    {
        if (DeviceConfigValidator.Validate(config) is [_, ..] errors)
        {
            throw new ArgumentException($"Cihaz kaydı geçersiz: {string.Join(" ", errors)}");
        }

        if (config.Wmi1 is not { } wmi)
        {
            throw new ArgumentException($"{config.Id}: wmi1 alan haritası yok; bu kayıt henüz okunamaz.");
        }

        var cpu = Fan(config, CpuFanId);
        var gpu = Fan(config, GpuFanId);
        var f = config.Features;
        string?[] missing =
        [
            f.CoolerBoost is null ? "coolerBoost" : null,
            f.ChargeLimit is null ? "chargeLimit" : null,
            f.PerformanceMode is null ? "performanceMode" : null,
            f.FanMode is null ? "fanMode" : null,
        ];
        if (missing.OfType<string>().ToArray() is [_, ..] absent)
        {
            throw new ArgumentException($"{config.Id}: {string.Join(", ", absent)} yok; bu kayıt henüz okunamaz.");
        }

        return new DeviceLayout(config, cpu, gpu, f, wmi);
    }

    private static FanRegisters Fan(DeviceConfig config, string id) =>
        config.Fans.FirstOrDefault(f => f.Id == id) is { } fan
            ? new FanRegisters(fan.Temperature, fan.SpeedPercent, fan.RpmHigh, fan.UpThresholds.Start, fan.Speeds.Start)
            : throw new ArgumentException($"{config.Id}: \"{CpuFanId}\" ve \"{GpuFanId}\" fanları gerekli; bu kayıt henüz okunamaz.");

    private static IEnumerable<byte> TableRegisters(FanRegisters fan) =>
        Enumerable.Range(fan.UpThresholdsStart, EcMap.ThresholdCount)
            .Concat(Enumerable.Range(fan.SpeedsStart, EcMap.SpeedCount))
            .Select(r => (byte)r);
}
