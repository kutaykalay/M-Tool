using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public class WritePlansTests
{
    [Fact]
    public void Encode_is_the_inverse_of_decode()
    {
        var tables = FanTableCodec.Encode(FactoryDefaults.FanCurves.Cpu);

        tables.UpThresholds.Should().Equal(55, 64, 70, 76, 82, 88);
        tables.Speeds.Should().Equal(45, 50, 60, 70, 75, 80, 80);
        tables.DownOffsets.Should().Equal(8, 3, 3, 3, 3, 3);
        FanTableCodec.Decode(tables.UpThresholds, tables.Speeds, tables.DownOffsets)
            .Points.Should().Equal(FactoryDefaults.FanCurves.Cpu.Points);
    }

    [Fact]
    public void Factory_defaults_match_the_table_read_from_the_laptop()
    {
        var fromEc = new P65Device(P65Memory.Faz0Snapshot()).ReadFanCurves();

        FactoryDefaults.FanCurves.Cpu.Points.Should().Equal(fromEc.Cpu.Points);
        FactoryDefaults.FanCurves.Gpu.Points.Should().Equal(fromEc.Gpu.Points);
    }

    [Fact]
    public void Fan_curve_plan_covers_all_38_table_registers()
    {
        var plan = WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default");

        plan.Writes.Should().HaveCount(38);
        plan.Writes.Select(w => w.Register).Should().OnlyHaveUniqueItems();
        plan.Writes.Should().Contain(new RegisterWrite(0x72, 45));
        plan.Writes.Should().Contain(new RegisterWrite(0x97, 5));
    }

    [Theory]
    [InlineData(true, 0x02, 0x82)]
    [InlineData(false, 0x82, 0x02)]
    [InlineData(true, 0x82, 0x82)]
    public void Cooler_boost_changes_only_bit_seven(bool on, byte current, byte expected)
    {
        WritePlans.CoolerBoost(on, current).Writes.Should().Equal(new RegisterWrite(0x98, expected));
    }

    [Theory]
    [InlineData(80, 0xD0)]
    [InlineData(50, 0xB2)]
    [InlineData(100, 0xE4)]
    public void Charge_limit_sets_the_enable_bit(int percent, byte expected)
    {
        WritePlans.ChargeLimit(percent).Writes.Should().Equal(new RegisterWrite(0xEF, expected));
    }

    [Theory]
    [InlineData(49)]
    [InlineData(101)]
    public void Charge_limit_rejects_out_of_range_values(int percent)
    {
        var act = () => WritePlans.ChargeLimit(percent);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PerformanceMode.High, 0xC0)]
    [InlineData(PerformanceMode.Balanced, 0xC1)]
    [InlineData(PerformanceMode.Eco, 0xC2)]
    public void Performance_modes_map_to_ec_values(PerformanceMode mode, byte expected)
    {
        WritePlans.Performance(mode).Writes.Should().Equal(new RegisterWrite(0xF2, expected));
    }

    [Theory]
    [InlineData(FanMode.Auto, 0x0D)]
    [InlineData(FanMode.Advanced, 0x8D)]
    public void Fan_modes_map_to_ec_values(FanMode mode, byte expected)
    {
        WritePlans.Fan(mode).Writes.Should().Equal(new RegisterWrite(0xF4, expected));
    }
}
