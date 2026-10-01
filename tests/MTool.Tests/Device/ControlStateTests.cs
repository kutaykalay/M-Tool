using MTool.Core.Device;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public class ControlStateTests
{
    [Fact]
    public void Reads_control_state_of_the_faz0_snapshot()
    {
        var state = new P65Device(P65Memory.Faz0Snapshot()).ReadControlState();

        state.FanCurves.Should().BeEquivalentTo(FactoryDefaults.FanCurves);
        state.Performance.Should().Be(PerformanceMode.High);
        state.PerformanceRaw.Should().Be(0xC0);
        state.FanMode.Should().Be(FanMode.Advanced);
        state.Port.Should().BeNull();
    }

    [Fact]
    public void Control_state_never_reads_the_port_registers()
    {
        var ec = P65Memory.Faz0Snapshot();
        var reads = new List<byte>();
        ec.ReadHook = register =>
        {
            reads.Add(register);
            return null;
        };

        new P65Device(ec).ReadControlState();

        reads.Should().NotContain([0x98, 0xEF]);
    }

    [Fact]
    public void Port_state_reads_cooler_boost_and_charge_limit_only()
    {
        var ec = P65Memory.Faz0Snapshot();
        var reads = new List<byte>();
        ec.ReadHook = register =>
        {
            reads.Add(register);
            return null;
        };

        var port = new P65Device(ec).ReadPortState();

        port.Should().Be(new PortState(CoolerBoostRaw: 0x02, ChargeLimitRaw: 0xD0));
        port.CoolerBoostOn.Should().BeFalse();
        port.ChargeLimitPercent.Should().Be(80);
        reads.Should().BeEquivalentTo([0x98, 0xEF]);
    }

    [Theory]
    [InlineData(0xC0, PerformanceMode.High)]
    [InlineData(0xC1, PerformanceMode.Balanced)]
    [InlineData(0xC2, PerformanceMode.Eco)]
    [InlineData(0x80, null)]
    [InlineData(0xC4, null)]
    public void Decodes_performance_mode_and_keeps_the_raw_byte(byte raw, PerformanceMode? expected)
    {
        var ec = P65Memory.Faz0Snapshot();
        ec.Load(0xF2, raw);

        var state = new P65Device(ec).ReadControlState();

        state.Performance.Should().Be(expected);
        state.PerformanceRaw.Should().Be(raw);
    }

    [Theory]
    [InlineData(0x82, true)]
    [InlineData(0x80, true)]
    [InlineData(0x02, false)]
    public void Cooler_boost_is_bit_7(byte raw, bool expected)
    {
        var ec = P65Memory.Faz0Snapshot();
        ec.Load(0x98, raw);

        new P65Device(ec).ReadPortState().CoolerBoostOn.Should().Be(expected);
    }

    [Theory]
    [InlineData(0xD0, 80)]
    [InlineData(0xB2, 50)]
    [InlineData(0xE4, 100)]
    [InlineData(0x50, null)]
    [InlineData(0x94, null)]
    [InlineData(0xE5, null)]
    public void Charge_limit_is_reported_only_when_enabled_and_in_range(byte raw, int? expected)
    {
        var ec = P65Memory.Faz0Snapshot();
        ec.Load(0xEF, raw);

        new P65Device(ec).ReadPortState().ChargeLimitPercent.Should().Be(expected);
    }

    [Theory]
    [InlineData(0x0D, FanMode.Auto)]
    [InlineData(0x8D, FanMode.Advanced)]
    [InlineData(0x4D, null)]
    public void Decodes_fan_mode(byte raw, FanMode? expected)
    {
        var ec = P65Memory.Faz0Snapshot();
        ec.Load(0xF4, raw);

        new P65Device(ec).ReadControlState().FanMode.Should().Be(expected);
    }

    [Theory]
    [InlineData(PerformanceMode.High)]
    [InlineData(PerformanceMode.Balanced)]
    [InlineData(PerformanceMode.Eco)]
    public void Performance_codes_round_trip(PerformanceMode mode)
    {
        ModeCodes.ToPerformance(ModeCodes.PerformanceByte(mode)).Should().Be(mode);
    }

    [Theory]
    [InlineData(FanMode.Auto)]
    [InlineData(FanMode.Advanced)]
    public void Fan_mode_codes_round_trip(FanMode mode)
    {
        ModeCodes.ToFanMode(ModeCodes.FanModeByte(mode)).Should().Be(mode);
    }
}
