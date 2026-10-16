using MTool.Core.Device;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

/// <summary>The temperature check shared by the WMI1 device and the WMI2 live view.</summary>
public class SensorReadTests
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(1, 1)]
    [InlineData(61, 61)]
    [InlineData(110, 110)]
    [InlineData(111, null)]
    [InlineData(255, null)]
    public void A_temperature_outside_1_to_110_degrees_is_not_a_reading(byte raw, int? expected)
    {
        var ec = new FakeEcRegisters();
        ec[0x68] = raw;

        SensorRead.Temperature(ec, 0x68).Should().Be(expected);
    }
}
