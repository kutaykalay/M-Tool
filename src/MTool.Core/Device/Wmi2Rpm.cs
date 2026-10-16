namespace MTool.Core.Device;

/// <summary>The raw tach registers of a WMI2 model: CPU 0xC8:0xC9, GPU 0xCA:0xCB.</summary>
public readonly record struct Wmi2Tach(byte CpuHigh, byte CpuLow, byte GpuHigh, byte GpuLow);

/// <summary>
/// The tach divisor on WMI2 models is one byte (the low register) or a big-endian pair, by model
/// (GhostDeck 7f3a670: 30 tested models one byte, 10 a pair). Both give RPM = 478000 / divisor and
/// agree while the high byte is 0. Otherwise a one-byte reading faster than any laptop fan means
/// the pair; anything else could be either, and no reading beats a wrong one.
/// </summary>
public static class Wmi2Rpm
{
    /// <summary>The fastest fan GhostDeck has logged is 7206 RPM (issue #92).</summary>
    public const int MaxPlausibleRpm = 8000;

    /// <summary>The RPM, 0 for a stopped fan, or null when it cannot be told.</summary>
    public static int? Resolve(byte high, byte low)
    {
        if (high == 0)
        {
            return Plausible(RpmCodec.ToRpm(0, low));
        }

        // A pair with a non-zero high byte is at least 256, so at most 1867 RPM: always plausible.
        var oneByte = RpmCodec.ToRpm(0, low);
        return oneByte > MaxPlausibleRpm ? RpmCodec.ToRpm(high, low) : null;
    }

    private static int? Plausible(int rpm) => rpm <= MaxPlausibleRpm ? rpm : null;
}
