using MTool.Core.Ec;

namespace MTool.Core.Device;

/// <summary>Sensor register reads shared by the WMI1 device and the WMI2 live view.</summary>
public static class SensorRead
{
    private const int MinPlausibleTempC = 1;
    private const int MaxPlausibleTempC = 110;

    /// <summary>Degrees Celsius, or null when the register holds no plausible temperature (0 when a GPU sleeps).</summary>
    public static int? Temperature(IEcRegisters ec, byte register)
    {
        int value = ec.Read(register);
        return value is >= MinPlausibleTempC and <= MaxPlausibleTempC ? value : null;
    }
}
