using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device;

/// <summary>Mode bytes are read with the record's own values, not the P65's (7f: A′ uses 0x0C/0x8C for the fan mode).</summary>
public class DeviceModeDecodingTests
{
    private static ControlState Read(DeviceConfig config, byte performance, byte fanMode)
    {
        var ec = P65Memory.FactorySnapshot();
        ec.Load(0xF2, performance);
        ec.Load(0xF4, fanMode);
        return new P65Device(ec, DeviceLayout.From(config)).ReadControlState();
    }

    private static DeviceConfig Legacy() => P65() with
    {
        Features = P65().Features with { FanMode = new FanModeFeature(0xF4, Auto: 0x0C, Advanced: 0x8C) },
    };

    [Theory]
    [InlineData(0x0C, FanMode.Auto)]
    [InlineData(0x8C, FanMode.Advanced)]
    public void The_fan_mode_uses_the_records_values(byte value, FanMode expected)
    {
        Read(Legacy(), 0xC1, value).FanMode.Should().Be(expected);
    }

    [Fact]
    public void A_p65_fan_mode_byte_means_nothing_on_a_record_with_other_values()
    {
        Read(Legacy(), 0xC1, 0x0D).FanMode.Should().BeNull();
    }

    [Fact]
    public void A_performance_byte_the_record_does_not_list_is_unknown()
    {
        var ecoOnly = P65() with
        {
            Features = P65().Features with { PerformanceMode = new PerformanceModeFeature(0xF2, [new ModeValue("eco", 0xC2)]) },
        };

        Read(ecoOnly, 0xC0, 0x0D).Performance.Should().BeNull();
        Read(ecoOnly, 0xC2, 0x0D).Performance.Should().Be(PerformanceMode.Eco);
    }

    [Theory]
    [InlineData(0xC0, PerformanceMode.High)]
    [InlineData(0xC1, PerformanceMode.Balanced)]
    [InlineData(0xC2, PerformanceMode.Eco)]
    public void The_p65_reads_as_before(byte value, PerformanceMode expected)
    {
        var state = Read(P65(), value, 0x8D);

        state.Performance.Should().Be(expected);
        state.PerformanceRaw.Should().Be(value);
        state.FanMode.Should().Be(FanMode.Advanced);
    }
}
