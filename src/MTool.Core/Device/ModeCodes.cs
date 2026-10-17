using MTool.Core.Ec;

namespace MTool.Core.Device;

public enum PerformanceMode
{
    High,
    Balanced,
    Eco,
}

public enum FanMode
{
    Auto,
    Advanced,
}

/// <summary>Byte values of the P65's mode registers, both directions. Unknown bytes decode to null.</summary>
public static class ModeCodes
{
    public const byte CoolerBoostBit = 0x80;
    public const byte ChargeLimitEnableBit = 0x80;
    public const int MaxKeyboardBacklightLevel = 3;

    private const byte KeyboardBacklightOff = 0x80;

    public static byte PerformanceByte(PerformanceMode mode) => mode switch
    {
        PerformanceMode.High => 0xC0,
        PerformanceMode.Balanced => 0xC1,
        PerformanceMode.Eco => 0xC2,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Null for anything else, e.g. 0x80 after a reboot (EC factory state).</summary>
    public static PerformanceMode? ToPerformance(byte value) => value switch
    {
        0xC0 => PerformanceMode.High,
        0xC1 => PerformanceMode.Balanced,
        0xC2 => PerformanceMode.Eco,
        _ => null,
    };

    public static byte FanModeByte(FanMode mode) => mode switch
    {
        FanMode.Auto => 0x0D,
        FanMode.Advanced => 0x8D,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static FanMode? ToFanMode(byte value) => value switch
    {
        0x0D => FanMode.Auto,
        0x8D => FanMode.Advanced,
        _ => null,
    };

    public static byte ChargeLimitByte(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, EcWriteRules.MinChargeLimitPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, EcWriteRules.MaxChargeLimitPercent);
        return (byte)(ChargeLimitEnableBit | percent);
    }

    /// <summary>Null when the limit is disabled (bit 7 clear) or outside the range M-Tool writes.</summary>
    public static int? ToChargeLimit(byte value)
    {
        if ((value & ChargeLimitEnableBit) == 0)
        {
            return null;
        }

        var percent = value & ~ChargeLimitEnableBit;
        return percent is >= EcWriteRules.MinChargeLimitPercent and <= EcWriteRules.MaxChargeLimitPercent ? percent : null;
    }

    public static bool IsCoolerBoostOn(byte value) => (value & CoolerBoostBit) != 0;

    /// <summary>Level 0 (off) to 3, as the Fn key sets it: 0x80-0x83.</summary>
    public static byte KeyboardBacklightByte(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, MaxKeyboardBacklightLevel);
        return (byte)(KeyboardBacklightOff + level);
    }
}
