using System.Collections.Frozen;
using MTool.Core.Device.Config;

namespace MTool.Core.Device;

/// <summary>
/// The registers M-Tool reads on one model, taken from its validated <see cref="DeviceConfig"/>.
/// The read side (router, device reader, dump) uses this; the write side keeps the P65's verified
/// statics (<see cref="EcMap"/>, <see cref="WmiMap"/>, <see cref="Ec.EcWriteRules"/>). A layout
/// needs two fans, a WMI1 map, the performance mode and the fan mode; Cooler Boost and the charge
/// limit are optional. A single-fan layout comes with the first single-fan record.
/// </summary>
public sealed class DeviceLayout
{
    private const string CpuFanId = "cpu";
    private const string GpuFanId = "gpu";

    private readonly FrozenDictionary<byte, PerformanceMode> _performanceModes;
    private readonly FrozenDictionary<byte, FanMode> _fanModes;

    private DeviceLayout(DeviceConfig config, FanRegisters cpu, FanRegisters gpu, FeatureSet features, Wmi1Layout wmi)
    {
        (Id, DisplayName, Status) = (config.Id, config.DisplayName, config.Status);
        Firmware = config.FirmwareLocation;
        CpuFan = cpu;
        GpuFan = gpu;
        CoolerBoost = features.CoolerBoost?.Register;
        ChargeLimit = features.ChargeLimit?.Register;
        PerformanceMode = features.PerformanceMode!.Register;
        FanMode = features.FanMode!.Register;
        Wmi = WmiFieldMap.From(config, wmi);
        Capabilities = DeviceCapabilities.From(config);
        _performanceModes = features.PerformanceMode!.Modes
            .Select(m => (m.Value, Mode: DeviceCapabilities.ModeOf(m.Id)))
            .Where(m => m.Mode is not null)
            .ToFrozenDictionary(m => m.Value, m => m.Mode!.Value);
        // "Device.FanMode": the enum, not this class's FanMode register property.
        _fanModes = new Dictionary<byte, FanMode>
        {
            [features.FanMode!.Auto] = Device.FanMode.Auto,
            [features.FanMode.Advanced] = Device.FanMode.Advanced,
        }.ToFrozenDictionary();
        WatchedRegisters = Array.AsReadOnly(
            [.. new[] { cpu, gpu }.SelectMany(TableRegisters).Append(PerformanceMode).Append(FanMode).Order()]);
    }

    /// <summary>A copy with other capabilities; registers and maps are shared, both are immutable.</summary>
    private DeviceLayout(DeviceLayout source, DeviceCapabilities capabilities)
    {
        (Id, DisplayName, Status) = (source.Id, source.DisplayName, source.Status);
        (Firmware, CpuFan, GpuFan) = (source.Firmware, source.CpuFan, source.GpuFan);
        (CoolerBoost, ChargeLimit, PerformanceMode, FanMode) = (source.CoolerBoost, source.ChargeLimit, source.PerformanceMode, source.FanMode);
        (Wmi, WatchedRegisters) = (source.Wmi, source.WatchedRegisters);
        (_performanceModes, _fanModes) = (source._performanceModes, source._fanModes);
        Capabilities = capabilities;
    }

    /// <summary>The record this layout came from, for the log.</summary>
    public string Id { get; }

    public string DisplayName { get; }

    /// <summary>How far the record was checked; anything below write-verified is shown as experimental.</summary>
    public DeviceStatus Status { get; }

    public FirmwareLocation Firmware { get; }

    public FanRegisters CpuFan { get; }

    public FanRegisters GpuFan { get; }

    /// <summary>Null when the model has no Cooler Boost.</summary>
    public byte? CoolerBoost { get; }

    /// <summary>Null when the model has no charge limit.</summary>
    public byte? ChargeLimit { get; }

    public byte PerformanceMode { get; }

    public byte FanMode { get; }

    public WmiFieldMap Wmi { get; }

    /// <summary>The controls the record has, for the UI.</summary>
    public DeviceCapabilities Capabilities { get; }

    /// <summary>Both fan tables, performance mode and fan mode, in register order: what a watch compares.</summary>
    public IReadOnlyList<byte> WatchedRegisters { get; }

    /// <summary>The record's performance mode for a byte; null for one it does not list (e.g. 0x80 after a reboot).</summary>
    public PerformanceMode? DecodePerformance(byte value) => _performanceModes.TryGetValue(value, out var mode) ? mode : null;

    /// <summary>The record's fan mode for a byte; null for anything else.</summary>
    public FanMode? DecodeFanMode(byte value) => _fanModes.TryGetValue(value, out var mode) ? mode : null;

    /// <summary>
    /// For a session without the raw port (an unverified model or firmware): Cooler Boost and the
    /// charge limit sit behind the port, so they are hidden, not shown as unknown.
    /// </summary>
    public DeviceLayout WithoutPortFeatures() =>
        new(this, Capabilities with { CoolerBoost = false, ChargeLimit = false });

    /// <exception cref="ArgumentException">
    /// The record is invalid or has no layout yet (one fan, no WMI1, no performance or fan mode). The message is
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
