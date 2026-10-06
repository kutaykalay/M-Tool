using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device.Config;

/// <summary>
/// Regression net for the move to device configs: today's statics must equal the literal
/// <see cref="P65Golden"/> tables. Green before any config code exists.
/// </summary>
public class P65GoldenTests
{
    [Fact]
    public void Firmware_and_its_location_match()
    {
        EcMap.SupportedFirmware.Should().Be(P65Golden.Firmware);
        EcMap.FirmwareVersion.Should().Be(P65Golden.FirmwareVersion);
        EcMap.FirmwareVersionLength.Should().Be(P65Golden.FirmwareVersionLength);
        EcMap.FirmwareDate.Should().Be(P65Golden.FirmwareDate);
        EcMap.FirmwareDateLength.Should().Be(P65Golden.FirmwareDateLength);
    }

    [Fact]
    public void Fan_registers_match()
    {
        Tuple(EcMap.CpuFan).Should().Be(P65Golden.CpuFan);
        Tuple(EcMap.GpuFan).Should().Be(P65Golden.GpuFan);
        EcMap.ThresholdCount.Should().Be(P65Golden.CpuUpThresholds.Length);
        EcMap.SpeedCount.Should().Be(P65Golden.CpuSpeeds.Length);
        EcMap.FanTableRegisters.Should().Equal(P65Golden.FanTableRegisters);
    }

    [Fact]
    public void Feature_registers_match()
    {
        EcMap.CoolerBoost.Should().Be(P65Golden.CoolerBoost);
        EcMap.ChargeLimit.Should().Be(P65Golden.ChargeLimit);
        EcMap.PerformanceMode.Should().Be(P65Golden.PerformanceMode);
        EcMap.FanMode.Should().Be(P65Golden.FanMode);
    }

    [Fact]
    public void Writable_registers_are_the_30_golden_ones()
    {
        EcWriteRules.WritableRegisters.Should().Equal(P65Golden.WritableRegisters);
    }

    [Fact]
    public void Fan_table_registers_are_exactly_the_26_table_bytes()
    {
        Enumerable.Range(0, 256).Select(r => (byte)r).Where(EcWriteRules.IsFanTable)
            .Should().Equal(P65Golden.FanTableRegisters);
    }

    [Fact]
    public void Write_order_matches()
    {
        P65Golden.WriteOrder.Select(e => (e.Register, EcWriteRules.WriteOrder(e.Register)))
            .Should().Equal(P65Golden.WriteOrder);
    }

    [Fact]
    public void Wmi_fields_are_the_56_golden_ones()
    {
        WmiMap.Fields.ToDictionary(f => f.Key, f => (f.Value.ClassName, f.Value.Index))
            .Should().BeEquivalentTo(P65Golden.WmiFields);
        WmiMap.Fields.Should().HaveCount(56);
    }

    [Fact]
    public void Port_registers_are_cooler_boost_and_charge_limit()
    {
        WmiMap.PortRegisters.Order().Should().Equal(P65Golden.PortRegisters);
    }

    [Fact]
    public void Factory_tables_and_down_offsets_match()
    {
        Points(FactoryDefaults.FanCurves.Cpu).Should().Equal(P65Golden.FactoryCpuCurve);
        Points(FactoryDefaults.FanCurves.Gpu).Should().Equal(P65Golden.FactoryGpuCurve);
        FactoryDefaults.CpuDownOffsets.Should().Equal(P65Golden.CpuDownOffsets);
        FactoryDefaults.GpuDownOffsets.Should().Equal(P65Golden.GpuDownOffsets);
    }

    [Fact]
    public void Presets_match()
    {
        Points(Presets.Cool.Curves.Cpu).Should().Equal(P65Golden.CoolCpuCurve);
        Points(Presets.Cool.Curves.Gpu).Should().Equal(P65Golden.CoolGpuCurve);
        Points(Presets.Silent.Curves.Cpu).Should().Equal(P65Golden.SilentCpuCurve);
        Points(Presets.Silent.Curves.Gpu).Should().Equal(P65Golden.SilentGpuCurve);
    }

    [Fact]
    public void Fan_curve_plans_write_the_golden_bytes_in_order()
    {
        Writes(WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default")).Should().Equal(P65Golden.DefaultPlan);
        Writes(WritePlans.FanCurves(Presets.Cool.Curves, "Cool")).Should().Equal(P65Golden.CoolPlan);
        Writes(WritePlans.FanCurves(Presets.Silent.Curves, "Silent")).Should().Equal(P65Golden.SilentPlan);
    }

    [Theory]
    [InlineData(true, 0x02, 0x82)]
    [InlineData(false, 0x82, 0x02)]
    public void Cooler_boost_plan_flips_bit_seven_of_0x98(bool on, byte current, byte expected)
    {
        Writes(WritePlans.CoolerBoost(on, current)).Should().Equal((P65Golden.CoolerBoost, expected));
    }

    [Theory]
    [InlineData(50, 0xB2)]
    [InlineData(80, 0xD0)]
    [InlineData(100, 0xE4)]
    public void Charge_limit_plan_writes_0x80_or_percent_to_0xEF(int percent, byte expected)
    {
        Writes(WritePlans.ChargeLimit(percent)).Should().Equal((P65Golden.ChargeLimit, expected));
        EcWriteRules.MinChargeLimitPercent.Should().Be(P65Golden.MinChargeLimitPercent);
        EcWriteRules.MaxChargeLimitPercent.Should().Be(P65Golden.MaxChargeLimitPercent);
    }

    [Fact]
    public void Mode_plans_write_the_golden_bytes()
    {
        P65Golden.PerformanceModes
            .Select(m => Writes(WritePlans.Performance(Enum.Parse<PerformanceMode>(m.Mode))).Single())
            .Should().Equal(P65Golden.PerformanceModes.Select(m => (P65Golden.PerformanceMode, m.Value)));
        P65Golden.FanModes
            .Select(m => Writes(WritePlans.Fan(Enum.Parse<FanMode>(m.Mode))).Single())
            .Should().Equal(P65Golden.FanModes.Select(m => (P65Golden.FanMode, m.Value)));
    }

    [Fact]
    public void Mode_enums_have_exactly_the_golden_modes()
    {
        Enum.GetNames<PerformanceMode>().Should().Equal(P65Golden.PerformanceModes.Select(m => m.Mode));
        Enum.GetNames<FanMode>().Should().Equal(P65Golden.FanModes.Select(m => m.Mode));
    }

    [Fact]
    public void Port_write_guard_watches_the_28_golden_registers()
    {
        var ec = new FakeEcRegisters();

        PortWriteGuard.Snapshot(ec).Keys.Should().Equal(P65Golden.PortGuardWatched);
    }

    [Fact]
    public void Port_write_guard_guards_only_plans_with_a_port_register()
    {
        var guarded = Enumerable.Range(0, 256).Select(r => (byte)r)
            .Where(r => PortWriteGuard.Guards(new WritePlan("test", [new RegisterWrite(r, 0)])));

        guarded.Should().Equal(P65Golden.PortRegisters);
    }

    [Fact]
    public void Static_rules_accept_and_reject_the_golden_boundaries()
    {
        P65Golden.StaticRules
            .Select(r => (r.Register, r.Value, EcWriteRules.CheckStatic(new RegisterWrite(r.Register, r.Value)) is null))
            .Should().Equal(P65Golden.StaticRules);
    }

    [Theory]
    [InlineData(0x82, 0x02, true)]
    [InlineData(0x02, 0x82, true)]
    [InlineData(0x83, 0x02, false)]
    [InlineData(0x80, 0x02, false)]
    public void Cooler_boost_write_may_change_only_bit_seven(byte value, byte current, bool accepted)
    {
        (EcWriteRules.CheckAgainstCurrent(new RegisterWrite(P65Golden.CoolerBoost, value), current) is null)
            .Should().Be(accepted);
    }

    private static (byte, byte, byte, byte, byte) Tuple(FanRegisters fan) =>
        (fan.Temperature, fan.SpeedPercent, fan.RpmHigh, fan.UpThresholdsStart, fan.SpeedsStart);

    private static IEnumerable<(int Up, int Speed)> Points(FanCurve curve) =>
        curve.Points.Select(p => (p.UpThresholdC, p.SpeedPercent));

    private static IEnumerable<(byte Register, byte Value)> Writes(WritePlan plan) =>
        plan.Writes.Select(w => (w.Register, w.Value));
}
