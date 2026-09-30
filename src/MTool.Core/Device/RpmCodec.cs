namespace MTool.Core.Device;

/// <summary>The EC reports the fan period as a big-endian 16-bit value; RPM = 478000 / period.</summary>
public static class RpmCodec
{
    private const int PeriodToRpmDividend = 478_000;

    public static int ToRpm(byte high, byte low)
    {
        var period = (high << 8) | low;
        return period == 0 ? 0 : PeriodToRpmDividend / period;
    }
}
