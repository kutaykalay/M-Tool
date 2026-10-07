using System.Text;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>Typed read access to the EC through a <see cref="DeviceLayout"/>. Knows the register map, never the protocol.</summary>
public sealed class P65Device
{
    private const int MinPlausibleTempC = 1;
    private const int MaxPlausibleTempC = 110;

    private readonly IEcRegisters _ec;
    private readonly DeviceLayout _layout;

    public P65Device(IEcRegisters ec, DeviceLayout layout) => (_ec, _layout) = (ec, layout);

    public FirmwareInfo ReadFirmware() => new(
        ReadAscii(_layout.Firmware.Version, _layout.Firmware.VersionLength),
        ReadAscii(_layout.Firmware.Date, _layout.Firmware.DateLength));

    public SensorSnapshot ReadSensors() => new(
        CpuTempC: ReadTemperature(_layout.CpuFan),
        GpuTempC: ReadTemperature(_layout.GpuFan),
        CpuFanPercent: _ec.Read(_layout.CpuFan.SpeedPercent),
        GpuFanPercent: _ec.Read(_layout.GpuFan.SpeedPercent),
        CpuRpm: ReadRpm(_layout.CpuFan),
        GpuRpm: ReadRpm(_layout.GpuFan));

    public FanCurves ReadFanCurves() => new(ReadFanCurve(_layout.CpuFan), ReadFanCurve(_layout.GpuFan));

    /// <summary>Everything but the port-only registers: <see cref="ControlState.Port"/> is null.</summary>
    public ControlState ReadControlState()
    {
        var performance = _ec.Read(_layout.PerformanceMode);
        return new ControlState(
            FanCurves: ReadFanCurves(),
            Performance: ModeCodes.ToPerformance(performance),
            PerformanceRaw: performance,
            FanMode: ModeCodes.ToFanMode(_ec.Read(_layout.FanMode)),
            Port: null);
    }

    /// <summary>Cooler Boost and the charge limit: two raw port accesses, so read them only when needed.</summary>
    /// <exception cref="InvalidOperationException">The model has no Cooler Boost or no charge limit: a port session is only opened for a record with both.</exception>
    public PortState ReadPortState() =>
        _layout is { CoolerBoost: { } boost, ChargeLimit: { } charge }
            ? new(_ec.Read(boost), _ec.Read(charge))
            : throw new InvalidOperationException($"{_layout.Id}: Cooler Boost ya da şarj limiti yok; port durumu okunamaz.");

    private FanCurve ReadFanCurve(FanRegisters fan) => FanTableCodec.Decode(
        _ec.ReadBlock(fan.UpThresholdsStart, EcMap.ThresholdCount),
        _ec.ReadBlock(fan.SpeedsStart, EcMap.SpeedCount));

    private int? ReadTemperature(FanRegisters fan)
    {
        int value = _ec.Read(fan.Temperature);
        return value is >= MinPlausibleTempC and <= MaxPlausibleTempC ? value : null;
    }

    private int ReadRpm(FanRegisters fan)
    {
        var bytes = _ec.ReadBlock(fan.RpmHigh, 2);
        return RpmCodec.ToRpm(bytes[0], bytes[1]);
    }

    private string ReadAscii(byte start, int length)
    {
        var bytes = _ec.ReadBlock(start, length).ToArray();
        var end = Array.IndexOf(bytes, (byte)0);
        var text = Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);

        // EC bytes reach the log, the window and dump files: no line breaks or escape sequences.
        return new string([.. text.Select(c => char.IsControl(c) ? '?' : c)]);
    }
}
