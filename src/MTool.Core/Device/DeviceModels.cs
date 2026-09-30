using MTool.Core.Profiles;

namespace MTool.Core.Device;

public sealed record FirmwareInfo(string Version, string Date)
{
    /// <summary>Writes are allowed only on the exact firmware the register map was verified on.</summary>
    public bool IsSupported => Version == EcMap.SupportedFirmware;
}

/// <summary>Live sensor values. A temperature outside the plausible range is reported as null.</summary>
public sealed record SensorSnapshot(
    int? CpuTempC,
    int? GpuTempC,
    int CpuFanPercent,
    int GpuFanPercent,
    int CpuRpm,
    int GpuRpm);

public sealed record FanCurves(FanCurve Cpu, FanCurve Gpu);
