using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Ec;

/// <summary>
/// The value rules of WMI2 experimental writes (plan asama15 15c-1): the code's ceiling and the firmware
/// list turn a logical change into one byte, and nothing else gets through.
/// </summary>
public class Wmi2WriteRulesTests
{
    private const string Standard = "15M3EMS1.105";
    private const string EcoC6 = "15M1IMS2.105";
    private const string HighC0 = "14D3EMS1.116";

    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.Load(
        """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105", "15M1IMS2.105", "14D3EMS1.116"],
          "families": ["15M3EMS1.1", "15M1IMS2.1", "14D3EMS1.1"],
          "familyOverrides": {
            "15M1IMS2.1": { "eco": "0xC6" },
            "14D3EMS1.1": { "high": "0xC0" }
          }
        }
        """,
        [P65()]);

    private static FirmwareInfo Fw(string version) => new(version, "");

    private static Wmi2Planned Plan(Wmi2Change change, byte current, string firmware = Standard) =>
        Wmi2WriteRules.Plan(change, current, List, Fw(firmware));

    private static Wmi2Rejection? Check(byte register, byte value, byte current, string firmware = Standard) =>
        Wmi2WriteRules.Check(new RegisterWrite(register, value), current, List, Fw(firmware));

    private static Wmi2Rejection? CheckRestore(byte register, byte value, byte current, string firmware = Standard) =>
        Wmi2WriteRules.CheckRestore(new RegisterWrite(register, value), current, List, Fw(firmware));

    private static void Writes(Wmi2Planned planned, byte register, byte value)
    {
        planned.Rejection.Should().BeNull();
        planned.Write.Should().Be(new RegisterWrite(register, value));
    }

    private static void Refuses(Wmi2Planned planned, Wmi2Rejection rejection)
    {
        planned.Write.Should().BeNull();
        planned.Rejection.Should().Be(rejection);
    }

    [Theory]
    [InlineData(true, 0x0A, 0x8A)]
    [InlineData(true, 0x8B, 0x8B)]
    [InlineData(false, 0x8A, 0x0A)]
    [InlineData(false, 0x0B, 0x0B)]
    public void Cooler_boost_sets_bit_7_and_keeps_the_low_bits(bool on, byte current, byte expected)
    {
        Writes(Plan(new Wmi2Change.CoolerBoost(on), current), 0x98, expected);
    }

    [Theory]
    [InlineData(0x8B)] // the low bits differ from the current byte
    [InlineData(0x0A)]
    public void A_cooler_boost_byte_that_changes_other_bits_is_refused(byte value)
    {
        Check(0x98, value, current: 0x89).Should().Be(Wmi2Rejection.OtherBitsChanged);
    }

    [Theory]
    [InlineData(50, 0xB2)]
    [InlineData(80, 0xD0)]
    [InlineData(100, 0xE4)]
    public void The_charge_limit_is_the_enable_bit_and_the_percent(int percent, byte expected)
    {
        Writes(Plan(new Wmi2Change.ChargeLimit(percent), current: 0xE4), 0xD7, expected);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(101)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(208)] // 0x80 | 208 would read as 80 %
    [InlineData(int.MaxValue)]
    public void A_charge_limit_outside_50_to_100_is_refused(int percent)
    {
        Refuses(Plan(new Wmi2Change.ChargeLimit(percent), current: 0xE4), Wmi2Rejection.ValueNotAllowed);
    }

    [Theory]
    [InlineData(0x50)] // 80 without the enable bit
    [InlineData(0xB1)] // 49 %
    [InlineData(0xE5)] // 101 %
    [InlineData(0x00)]
    public void A_charge_byte_outside_the_rule_is_refused(byte value)
    {
        Check(0xD7, value, current: 0xE4).Should().Be(Wmi2Rejection.ValueNotAllowed);
    }

    [Theory]
    [InlineData(0x8A)] // 10 %
    [InlineData(0x9E)] // 30 %
    [InlineData(0xB1)] // 49 %
    [InlineData(0xD0)] // 80 %
    public void A_saved_charge_limit_the_layout_allows_may_be_written_back(byte value)
    {
        CheckRestore(0xD7, value, current: 0xD0).Should().BeNull();
    }

    [Theory]
    [InlineData(0x89)] // 9 %
    [InlineData(0xE5)] // 101 %
    [InlineData(0x50)] // 80 without the enable bit
    [InlineData(0x00)]
    public void A_charge_byte_outside_the_layout_is_not_written_back(byte value)
    {
        CheckRestore(0xD7, value, current: 0xD0).Should().Be(Wmi2Rejection.ValueNotAllowed);
    }

    [Theory]
    [InlineData(0xD2, 0xC5, 0xC1)] // MSI Center's fourth mode
    [InlineData(0xD4, 0x8D, 0x0D)] // advanced
    [InlineData(0x98, 0x83, 0x02)] // other bits
    public void Writing_back_widens_nothing_but_the_charge_limit(byte register, byte value, byte current)
    {
        CheckRestore(register, value, current).Should().Be(Check(register, value, current)).And.NotBeNull();
    }

    [Fact]
    public void Writing_back_keeps_every_other_rule()
    {
        CheckRestore(0xD7, 0x9E, current: 0xD0, firmware: "15M3EMS1.199").Should().Be(Wmi2Rejection.NotExactFirmware);
        CheckRestore(0xD7, 0x9E, current: 0x00).Should().Be(Wmi2Rejection.CurrentOutsideLayout);
        CheckRestore(0xEB, 0x0F, current: 0x00).Should().Be(Wmi2Rejection.NotWritable);
    }

    [Theory]
    [InlineData(PerformanceMode.Balanced, 0xC1)]
    [InlineData(PerformanceMode.Eco, 0xC2)]
    [InlineData(PerformanceMode.High, 0xC4)]
    public void A_standard_family_gets_the_standard_performance_bytes(PerformanceMode mode, byte expected)
    {
        Writes(Plan(new Wmi2Change.Performance(mode), current: 0xC1), 0xD2, expected);
    }

    [Theory]
    [InlineData(EcoC6, PerformanceMode.Eco, 0xC6)]
    [InlineData(EcoC6, PerformanceMode.High, 0xC4)]
    [InlineData(HighC0, PerformanceMode.High, 0xC0)]
    [InlineData(HighC0, PerformanceMode.Eco, 0xC2)]
    public void A_family_override_replaces_the_byte_of_its_mode_only(string firmware, PerformanceMode mode, byte expected)
    {
        Writes(Plan(new Wmi2Change.Performance(mode), current: 0xC1, firmware), 0xD2, expected);
    }

    [Theory]
    [InlineData(Standard, 0xC0)] // C0 only where the family's override is C0
    [InlineData(Standard, 0xC6)]
    [InlineData(HighC0, 0xC4)] // that family's high is C0
    [InlineData(EcoC6, 0xC2)] // that family's eco is C6
    [InlineData(Standard, 0xC5)] // MSI Center's fourth mode: read, never written
    [InlineData(Standard, 0x00)]
    public void A_performance_byte_the_family_does_not_use_is_refused(string firmware, byte value)
    {
        Check(0xD2, value, current: 0xC1, firmware).Should().Be(Wmi2Rejection.ValueNotAllowed);
    }

    [Fact]
    public void An_undefined_performance_mode_is_refused()
    {
        Refuses(Plan(new Wmi2Change.Performance((PerformanceMode)7), current: 0xC1), Wmi2Rejection.ValueNotAllowed);
    }

    [Theory]
    [InlineData(Wmi2FanMode.Auto, 0x0D)]
    [InlineData(Wmi2FanMode.Silent, 0x1D)]
    public void Auto_and_silent_fan_modes_are_written(Wmi2FanMode mode, byte expected)
    {
        Writes(Plan(new Wmi2Change.FanMode(mode), current: 0x0D), 0xD4, expected);
    }

    [Fact]
    public void The_advanced_fan_mode_is_never_written()
    {
        Refuses(Plan(new Wmi2Change.FanMode(Wmi2FanMode.Advanced), current: 0x0D), Wmi2Rejection.AdvancedFanMode);
    }

    [Fact]
    public void An_undefined_fan_mode_is_refused()
    {
        Refuses(Plan(new Wmi2Change.FanMode((Wmi2FanMode)7), current: 0x0D), Wmi2Rejection.ValueNotAllowed);
    }

    [Theory]
    [InlineData(0x8D)]
    [InlineData(0x9D)]
    [InlineData(0x00)]
    public void A_fan_mode_byte_other_than_auto_or_silent_is_refused(byte value)
    {
        Check(0xD4, value, current: 0x0D).Should().Be(Wmi2Rejection.ValueNotAllowed);
    }

    [Theory]
    [InlineData(0xEB)] // Super Battery: deferred
    [InlineData(0x6A)] // fan table
    [InlineData(0xA0)]
    [InlineData(0xF2)] // the P65 performance register
    [InlineData(0xEF)]
    public void A_register_outside_the_ceiling_is_refused(byte register)
    {
        Check(register, 0x0F, current: 0x00).Should().Be(Wmi2Rejection.NotWritable);
    }

    [Fact]
    public void A_performance_change_is_refused_while_0xD2_reads_outside_the_layout()
    {
        Refuses(Plan(new Wmi2Change.Performance(PerformanceMode.High), current: 0x00), Wmi2Rejection.CurrentOutsideLayout);
        Check(0xD2, 0xC4, current: 0x00).Should().Be(Wmi2Rejection.CurrentOutsideLayout);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x50)]
    public void A_charge_change_is_refused_while_0xD7_reads_outside_the_layout(byte current)
    {
        Refuses(Plan(new Wmi2Change.ChargeLimit(80), current), Wmi2Rejection.CurrentOutsideLayout);
    }

    [Fact]
    public void A_fan_mode_change_is_refused_while_0xD4_reads_outside_the_layout()
    {
        Refuses(Plan(new Wmi2Change.FanMode(Wmi2FanMode.Silent), current: 0x2D), Wmi2Rejection.CurrentOutsideLayout);
    }

    public static TheoryData<Wmi2Change, byte> EveryChange => new()
    {
        { new Wmi2Change.CoolerBoost(true), 0x0A },
        { new Wmi2Change.Performance(PerformanceMode.High), 0xC1 },
        { new Wmi2Change.FanMode(Wmi2FanMode.Silent), 0x0D },
        { new Wmi2Change.ChargeLimit(80), 0xE4 },
    };

    [Theory]
    [MemberData(nameof(EveryChange))]
    public void Every_change_is_refused_on_a_family_match(Wmi2Change change, byte current)
    {
        Refuses(Wmi2WriteRules.Plan(change, current, List, Fw("15M3EMS1.199")), Wmi2Rejection.NotExactFirmware);
    }

    [Theory]
    [MemberData(nameof(EveryChange))]
    public void Every_change_is_refused_on_firmware_the_list_does_not_know(Wmi2Change change, byte current)
    {
        Refuses(Wmi2WriteRules.Plan(change, current, List, Fw("14F1EMS1.112")), Wmi2Rejection.NotExactFirmware);
        Refuses(Wmi2WriteRules.Plan(change, current, List, Fw("")), Wmi2Rejection.NotExactFirmware);
        Refuses(Wmi2WriteRules.Plan(change, current, List, null), Wmi2Rejection.NotExactFirmware);
    }

    [Fact]
    public void Check_also_refuses_a_valid_byte_on_a_family_match()
    {
        Check(0xD2, 0xC4, current: 0xC1, firmware: "15M3EMS1.199").Should().Be(Wmi2Rejection.NotExactFirmware);
    }

    [Fact]
    public void The_firmware_comes_before_the_register_in_the_checks()
    {
        Check(0xEB, 0x0F, current: 0x00, firmware: "15M3EMS1.199").Should().Be(Wmi2Rejection.NotExactFirmware);
    }

    [Theory]
    [InlineData(0x98, 0x8A, 0x0A)]
    [InlineData(0xD2, 0xC4, 0xC1)]
    [InlineData(0xD4, 0x1D, 0x0D)]
    [InlineData(0xD7, 0xD0, 0xE4)]
    public void A_byte_inside_every_rule_passes(byte register, byte value, byte current)
    {
        Check(register, value, current).Should().BeNull();
    }

    [Fact]
    public void A_planned_write_targets_the_register_of_its_change()
    {
        foreach (var (change, current) in new (Wmi2Change, byte)[]
        {
            (new Wmi2Change.CoolerBoost(true), 0x0A),
            (new Wmi2Change.Performance(PerformanceMode.Eco), 0xC1),
            (new Wmi2Change.FanMode(Wmi2FanMode.Auto), 0x1D),
            (new Wmi2Change.ChargeLimit(60), 0xE4),
        })
        {
            Plan(change, current).Write!.Register.Should().Be(change.Register);
        }
    }

    [Theory]
    [InlineData(0x98, 0x80)]
    [InlineData(0xD2, 0xFF)]
    [InlineData(0xD4, 0xFF)]
    [InlineData(0xD7, 0xFF)]
    public void Only_bit_7_of_cooler_boost_is_read_back(byte register, byte mask)
    {
        Wmi2WriteRules.MaskOf(register).Should().Be(mask);
    }

    [Fact]
    public void A_register_outside_the_ceiling_has_no_mask()
    {
        var act = () => Wmi2WriteRules.MaskOf(0xEB);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_charge_change_from_a_low_limit_the_layout_allows_is_written()
    {
        Writes(Plan(new Wmi2Change.ChargeLimit(80), current: 0x8A), 0xD7, 0xD0); // 10 %: read, never written
    }

    [Fact]
    public void Every_planned_write_passes_the_gateways_second_check()
    {
        Wmi2Change[] changes =
        [
            new Wmi2Change.CoolerBoost(true),
            new Wmi2Change.CoolerBoost(false),
            .. Enum.GetValues<PerformanceMode>().Select(m => new Wmi2Change.Performance(m)),
            .. Enum.GetValues<Wmi2FanMode>().Select(m => new Wmi2Change.FanMode(m)),
            .. Enumerable.Range(45, 60).Select(p => new Wmi2Change.ChargeLimit(p)),
        ];

        foreach (var firmware in new[] { Standard, EcoC6, HighC0 })
        {
            foreach (var change in changes)
            {
                for (var current = 0; current <= byte.MaxValue; current++)
                {
                    if (Plan(change, (byte)current, firmware).Write is { } write)
                    {
                        Check(write.Register, write.Value, (byte)current, firmware).Should().BeNull($"{change} over 0x{current:X2} on {firmware}");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(Standard, 0xC1)]
    [InlineData(Standard, 0xC4)]
    [InlineData(EcoC6, 0xC6)]
    [InlineData(HighC0, 0xC0)]
    public void The_write_path_test_writes_the_current_performance_byte_again(string firmware, byte current)
    {
        Wmi2WriteRules.CheckWritePathTest(new RegisterWrite(0xD2, current), current, List, Fw(firmware)).Should().BeNull();
    }

    [Theory]
    [InlineData(0xD2, 0xC4, 0xC1)] // a real change
    [InlineData(0xD4, 0x0D, 0x0D)] // the same byte, but not the performance register
    [InlineData(0xD7, 0xE4, 0xE4)]
    [InlineData(0x98, 0x0A, 0x0A)]
    public void A_write_path_test_that_is_not_the_current_performance_byte_is_refused(byte register, byte value, byte current)
    {
        Wmi2WriteRules.CheckWritePathTest(new RegisterWrite(register, value), current, List, Fw(Standard))
            .Should().Be(Wmi2Rejection.NotAWritePathTest);
    }

    [Theory]
    [InlineData(Standard, 0xC5, Wmi2Rejection.ValueNotAllowed)] // MSI Center's fourth mode is not rewritten
    [InlineData(EcoC6, 0xC2, Wmi2Rejection.ValueNotAllowed)] // not this family's byte
    [InlineData(Standard, 0x00, Wmi2Rejection.CurrentOutsideLayout)]
    [InlineData("15M3EMS1.199", 0xC1, Wmi2Rejection.NotExactFirmware)]
    public void A_write_path_test_must_also_pass_every_rule(string firmware, byte current, Wmi2Rejection rejection)
    {
        Wmi2WriteRules.CheckWritePathTest(new RegisterWrite(0xD2, current), current, List, Fw(firmware)).Should().Be(rejection);
    }

    [Fact]
    public void A_list_with_an_override_the_code_does_not_allow_refuses_instead_of_throwing()
    {
        var unvalidated = Wmi2FirmwareList.Parse(
            """
            {
              "schemaVersion": 1,
              "sources": ["test"],
              "exact": ["15M1IMS2.105"],
              "families": ["15M1IMS2.1"],
              "familyOverrides": { "15M1IMS2.1": { "eco": "0xC0" } }
            }
            """);

        Refuses(Wmi2WriteRules.Plan(new Wmi2Change.Performance(PerformanceMode.Eco), 0xC1, unvalidated, Fw(EcoC6)), Wmi2Rejection.ListInvalid);
        Wmi2WriteRules.Check(new RegisterWrite(0xD2, 0xC0), 0xC1, unvalidated, Fw(EcoC6)).Should().Be(Wmi2Rejection.ListInvalid);
        Wmi2WriteRules.CheckWritePathTest(new RegisterWrite(0xD2, 0xC1), 0xC1, unvalidated, Fw(EcoC6)).Should().Be(Wmi2Rejection.ListInvalid);
    }

    [Fact]
    public void A_list_with_a_malformed_exact_entry_refuses_instead_of_throwing()
    {
        var unvalidated = new Wmi2FirmwareList(1, ["test"], ["15M"], [], new Dictionary<string, IReadOnlyDictionary<PerformanceMode, byte>>());

        Refuses(Wmi2WriteRules.Plan(new Wmi2Change.Performance(PerformanceMode.High), 0xC1, unvalidated, Fw("15M")), Wmi2Rejection.ListInvalid);
        Wmi2WriteRules.Check(new RegisterWrite(0xD2, 0xC4), 0xC1, unvalidated, Fw("15M")).Should().Be(Wmi2Rejection.ListInvalid);
    }

    [Fact]
    public void Missing_arguments_are_refused()
    {
        var change = new Wmi2Change.CoolerBoost(true);
        var write = new RegisterWrite(0x98, 0x8A);

        ((Action)(() => Wmi2WriteRules.Plan(null!, 0, List, Fw(Standard)))).Should().Throw<ArgumentNullException>();
        ((Action)(() => Wmi2WriteRules.Plan(change, 0, null!, Fw(Standard)))).Should().Throw<ArgumentNullException>();
        ((Action)(() => Wmi2WriteRules.Check(null!, 0, List, Fw(Standard)))).Should().Throw<ArgumentNullException>();
        ((Action)(() => Wmi2WriteRules.Check(write, 0, null!, Fw(Standard)))).Should().Throw<ArgumentNullException>();
        ((Action)(() => Wmi2Planned.Of(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Every_change_names_a_register_of_the_ceiling()
    {
        new Wmi2Change[]
        {
            new Wmi2Change.CoolerBoost(true),
            new Wmi2Change.Performance(PerformanceMode.High),
            new Wmi2Change.FanMode(Wmi2FanMode.Auto),
            new Wmi2Change.ChargeLimit(80),
        }.Select(c => c.Register).Should().Equal(Wmi2WriteCeiling.Registers);
    }
}
