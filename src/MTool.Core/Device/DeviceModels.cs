namespace MTool.Core.Device;

public sealed record FirmwareInfo(string Version, string Date)
{
    /// <summary>Writes are allowed only on the exact firmware the register map was verified on.</summary>
    public bool IsSupported => Version == EcMap.SupportedFirmware;
}

/// <summary>
/// Live sensor values. A temperature outside the plausible range is reported as null. Registers
/// are read one by one, so values in one snapshot may be a few milliseconds apart.
/// </summary>
/// <param name="Tach">WMI2 only: the raw tach bytes, because the RPM may be unclear (<see cref="Wmi2Rpm"/>).</param>
public sealed record SensorSnapshot(
    int? CpuTempC,
    int? GpuTempC,
    int CpuFanPercent,
    int GpuFanPercent,
    int CpuRpm,
    int GpuRpm,
    Wmi2Tach? Tach = null);
