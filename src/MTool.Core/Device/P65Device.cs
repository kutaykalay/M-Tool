using System.Text;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>Typed read access to the P65's EC. Knows the register map, never the protocol.</summary>
public sealed class P65Device
{
    private const int MinPlausibleTempC = 1;
    private const int MaxPlausibleTempC = 110;

    private readonly IEcRegisters _ec;

    public P65Device(IEcRegisters ec) => _ec = ec;

    public FirmwareInfo ReadFirmware() => new(
        ReadAscii(EcMap.FirmwareVersion, EcMap.FirmwareVersionLength),
        ReadAscii(EcMap.FirmwareDate, EcMap.FirmwareDateLength));

    public SensorSnapshot ReadSensors() => new(
        CpuTempC: ReadTemperature(EcMap.CpuFan),
        GpuTempC: ReadTemperature(EcMap.GpuFan),
        CpuFanPercent: _ec.Read(EcMap.CpuFan.SpeedPercent),
        GpuFanPercent: _ec.Read(EcMap.GpuFan.SpeedPercent),
        CpuRpm: ReadRpm(EcMap.CpuFan),
        GpuRpm: ReadRpm(EcMap.GpuFan));

    public FanCurves ReadFanCurves() => new(ReadFanCurve(EcMap.CpuFan), ReadFanCurve(EcMap.GpuFan));

    /// <summary>Everything but the port-only registers: <see cref="ControlState.Port"/> is null.</summary>
    public ControlState ReadControlState()
    {
        var performance = _ec.Read(EcMap.PerformanceMode);
        return new ControlState(
            FanCurves: ReadFanCurves(),
            Performance: ModeCodes.ToPerformance(performance),
            PerformanceRaw: performance,
            FanMode: ModeCodes.ToFanMode(_ec.Read(EcMap.FanMode)),
            Port: null);
    }

    /// <summary>Cooler Boost and the charge limit: two raw port accesses, so read them only when needed.</summary>
    public PortState ReadPortState() => new(_ec.Read(EcMap.CoolerBoost), _ec.Read(EcMap.ChargeLimit));

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
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }
}
