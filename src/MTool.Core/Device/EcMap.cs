namespace MTool.Core.Device;

/// <summary>Register addresses of one fan's live values and its table in EC memory.</summary>
public sealed record FanRegisters(
    byte Temperature,
    byte SpeedPercent,
    byte RpmHigh,
    byte UpThresholdsStart,
    byte SpeedsStart,
    byte DownOffsetsStart);

/// <summary>
/// EC register map for MSI P65 Creator 9SE, firmware 16Q4EMS2.107. Verified against YAMDCC's
/// working config and a read-only dump in Faz 0 (docs/plans/faz0-arastirma.md §6).
/// </summary>
public static class EcMap
{
    public const string SupportedFirmware = "16Q4EMS2.107";

    public const byte FirmwareVersion = 0xA0;
    public const int FirmwareVersionLength = 12;
    public const byte FirmwareDate = 0xAC;
    public const int FirmwareDateLength = 8;

    public const int ThresholdCount = 6;
    public const int SpeedCount = 7;

    public static FanRegisters CpuFan { get; } = new(
        Temperature: 0x68, SpeedPercent: 0x71, RpmHigh: 0xCC,
        UpThresholdsStart: 0x6A, SpeedsStart: 0x72, DownOffsetsStart: 0x7A);

    public static FanRegisters GpuFan { get; } = new(
        Temperature: 0x80, SpeedPercent: 0x89, RpmHigh: 0xCA,
        UpThresholdsStart: 0x82, SpeedsStart: 0x8A, DownOffsetsStart: 0x92);

    public const byte CoolerBoost = 0x98;
    public const byte ChargeLimit = 0xEF;
    public const byte PerformanceMode = 0xF2;
    public const byte FanMode = 0xF4;
}
