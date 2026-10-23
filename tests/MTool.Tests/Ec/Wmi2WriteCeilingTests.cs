using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Tests.Ec;

/// <summary>The code's fixed ceiling for WMI2 writes (plan asama15 §4.2): no record can widen it.</summary>
public class Wmi2WriteCeilingTests
{
    [Fact]
    public void The_registers_are_the_status_registers_the_family_reads()
    {
        Wmi2WriteCeiling.Registers.Should().Equal(Wmi2Family.StatusRegisters);
        Wmi2WriteCeiling.Registers.Should().Equal(new byte[] { 0x98, 0xD2, 0xD4, 0xD7 });
    }

    [Fact]
    public void Cooler_boost_is_bit_7_of_0x98_only()
    {
        Wmi2WriteCeiling.CoolerBoostRegister.Should().Be(0x98);
        Wmi2WriteCeiling.CoolerBoostMask.Should().Be(0x80);
    }

    [Fact]
    public void The_standard_performance_bytes_are_balanced_c1_eco_c2_high_c4()
    {
        Wmi2WriteCeiling.PerformanceRegister.Should().Be(0xD2);
        Wmi2WriteCeiling.StandardPerformance.Should().BeEquivalentTo(new Dictionary<PerformanceMode, byte>
        {
            [PerformanceMode.Balanced] = 0xC1,
            [PerformanceMode.Eco] = 0xC2,
            [PerformanceMode.High] = 0xC4,
        });
    }

    [Fact]
    public void The_only_exceptions_are_eco_c6_and_high_c0()
    {
        Wmi2WriteCeiling.PerformanceExceptions.Should().BeEquivalentTo(new Dictionary<PerformanceMode, byte>
        {
            [PerformanceMode.Eco] = 0xC6,
            [PerformanceMode.High] = 0xC0,
        });
    }

    [Theory]
    [InlineData(PerformanceMode.Eco, 0xC6, true)]
    [InlineData(PerformanceMode.High, 0xC0, true)]
    [InlineData(PerformanceMode.Eco, 0xC0, false)] // no swapped meanings
    [InlineData(PerformanceMode.High, 0xC6, false)]
    [InlineData(PerformanceMode.Balanced, 0xC0, false)]
    [InlineData(PerformanceMode.Balanced, 0xC6, false)]
    [InlineData(PerformanceMode.Eco, 0xC2, false)] // the standard byte is not an exception
    [InlineData(PerformanceMode.High, 0xC5, false)] // MSI Center's fourth mode: read, never written
    [InlineData((PerformanceMode)7, 0xC0, false)]
    public void An_exception_belongs_to_one_mode(PerformanceMode mode, byte value, bool allowed)
    {
        Wmi2WriteCeiling.IsPerformanceException(mode, value).Should().Be(allowed);
    }

    [Theory]
    [InlineData(0xC5)]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void Other_performance_bytes_are_in_neither_table(byte value)
    {
        Wmi2WriteCeiling.StandardPerformance.Values.Should().NotContain(value);
        Wmi2WriteCeiling.PerformanceExceptions.Values.Should().NotContain(value);
    }

    [Fact]
    public void Fan_mode_writes_only_auto_and_silent()
    {
        Wmi2WriteCeiling.FanModeRegister.Should().Be(0xD4);
        Wmi2WriteCeiling.FanModeValues.Should().BeEquivalentTo(new Dictionary<Wmi2FanMode, byte>
        {
            [Wmi2FanMode.Auto] = 0x0D,
            [Wmi2FanMode.Silent] = 0x1D,
        });
    }

    [Fact]
    public void The_charge_limit_uses_the_p65_range_with_the_enable_bit()
    {
        Wmi2WriteCeiling.ChargeLimitRegister.Should().Be(0xD7);
        Wmi2WriteCeiling.ChargeLimitEnableMask.Should().Be(0x80);
        Wmi2WriteCeiling.MinChargeLimitPercent.Should().Be(50);
        Wmi2WriteCeiling.MaxChargeLimitPercent.Should().Be(100);
    }

    [Fact]
    public void Super_battery_is_not_in_the_ceiling()
    {
        Wmi2WriteCeiling.Registers.Should().NotContain(0xEB);
    }
}
