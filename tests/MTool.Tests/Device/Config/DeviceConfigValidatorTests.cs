using MTool.Core.Device.Config;
using MTool.Core.Profiles;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device.Config;

public class DeviceConfigValidatorTests
{
    [Fact]
    public void The_p65_fixture_is_valid()
    {
        DeviceConfigValidator.Validate(P65()).Should().BeEmpty();
    }

    [Fact]
    public void A_minimal_draft_without_firmware_list_offsets_or_curves_is_valid()
    {
        DeviceConfigValidator.Validate(Draft()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Rejects_an_unknown_schema_version(int version)
    {
        Rejects(P65() with { SchemaVersion = version }, "*schemaVersion*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("MSI P65")]
    [InlineData("../p65")]
    public void Rejects_an_id_that_is_not_lowercase_kebab_case(string id)
    {
        Rejects(P65() with { Id = id }, "*id*");
    }

    [Fact]
    public void Rejects_an_empty_display_name()
    {
        Rejects(P65() with { DisplayName = " " }, "*displayName*");
    }

    [Fact]
    public void Rejects_a_config_without_fans()
    {
        Rejects(P65() with { Fans = [] }, "*En az bir fan*");
    }

    [Fact]
    public void Rejects_duplicate_fan_ids()
    {
        Rejects(P65() with { Fans = [CpuFan(), GpuFan() with { Id = "cpu" }] }, "*fan id \"cpu\" iki kez*");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Rejects_a_threshold_block_that_is_not_six_registers(int count)
    {
        var cpu = CpuFan() with { UpThresholds = new RegisterBlock(0x6A, count) };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*yukarı eşik bloğu 6 register*");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void Rejects_a_speed_block_that_is_not_seven_registers(int count)
    {
        var cpu = CpuFan() with { Speeds = new RegisterBlock(0x72, count) };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*hız bloğu 7 register*");
    }

    [Fact]
    public void Rejects_a_block_that_runs_past_0xFF()
    {
        var gpu = GpuFan() with { Speeds = new RegisterBlock(0xFC, 7) };

        Rejects(P65() with { Fans = [CpuFan(), gpu] }, "*0xFC ile başlayan hız bloğu*");
    }

    [Fact]
    public void Rejects_overlapping_fan_blocks()
    {
        var gpu = GpuFan() with { UpThresholds = new RegisterBlock(0x6F, 6) };

        Rejects(P65() with { Fans = [CpuFan(), gpu] }, "*0x6F birden çok yerde*");
    }

    [Fact]
    public void Rejects_a_register_used_twice()
    {
        var gpu = GpuFan() with { Temperature = 0x68 };

        Rejects(P65() with { Fans = [CpuFan(), gpu] }, "*0x68 birden çok yerde*");
    }

    [Fact]
    public void Rejects_a_feature_register_that_is_also_a_fan_register()
    {
        var features = P65().Features with { FanMode = new FanModeFeature(0x72, 0x0D, 0x8D) };

        Rejects(P65() with { Features = features }, "*0x72 birden çok yerde*");
    }

    [Fact]
    public void Rejects_an_rpm_low_byte_that_collides_with_another_register()
    {
        var gpu = GpuFan() with { RpmHigh = 0xCB };

        Rejects(P65() with { Fans = [CpuFan(), gpu] }, "*0xCC birden çok yerde*");
    }

    [Theory]
    [InlineData(29, 95, 100)]
    [InlineData(30, 96, 100)]
    [InlineData(30, 95, 101)]
    [InlineData(60, 50, 100)]
    public void Rejects_limits_beyond_the_code_ceiling(int minUp, int maxUp, int maxSpeed)
    {
        Rejects(P65() with { Limits = new CurveLimits(minUp, maxUp, maxSpeed) }, "*limits*");
    }

    [Fact]
    public void Rejects_a_factory_curve_with_a_missing_point()
    {
        var cpu = CpuFan() with { FactoryCurve = Curve((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (88, 80)) };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*cpu fabrika eğrisi: Eğri 7 noktadan*");
    }

    [Fact]
    public void Rejects_a_factory_curve_with_an_extra_point()
    {
        var cpu = CpuFan() with
        {
            FactoryCurve = Curve((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80), (90, 100)),
        };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*cpu fabrika eğrisi: Eğri 7 noktadan*");
    }

    [Fact]
    public void Rejects_a_factory_curve_above_the_speed_ceiling()
    {
        var cpu = CpuFan() with { FactoryCurve = Curve((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (88, 101)) };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*cpu fabrika eğrisi: Nokta 7: fan hızı*");
    }

    [Fact]
    public void Rejects_a_factory_curve_that_fails_with_its_own_down_offsets()
    {
        // 88 - 10 = 78 does not clear the 82 °C step below it.
        var cpu = CpuFan() with { FactoryDownOffsets = [8, 3, 3, 3, 3, 10] };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*cpu fabrika eğrisi: Nokta 7: fan bu noktadan 78*");
    }

    [Fact]
    public void Rejects_down_offsets_that_are_not_six_values()
    {
        var cpu = CpuFan() with { FactoryDownOffsets = [8, 3, 3, 3, 3] };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*cpu: iniş ofsetleri*");
    }

    [Fact]
    public void Rejects_a_curve_outside_the_records_own_limits()
    {
        // Factory CPU curve reaches 88 °C; the record allows only up to 85.
        Rejects(P65() with { Limits = new CurveLimits(30, 85, 100) }, "*cpu fabrika eğrisi: kaydın limitleri dışında*");
    }

    [Fact]
    public void Rejects_a_preset_that_fails_with_the_records_down_offsets()
    {
        // Silent GPU is 83 -> 89 °C; a 7 °C last offset drops it back at 82.
        var gpu = GpuFan() with { FactoryDownOffsets = [8, 3, 3, 3, 3, 7] };

        Rejects(P65() with { Fans = [CpuFan(), gpu] }, "*Silent (gpu): Nokta 7: fan bu noktadan 82*");
    }

    [Fact]
    public void Rejects_a_preset_for_an_unknown_fan()
    {
        var preset = new PresetConfig("Odd", new Dictionary<string, FanCurve>
        {
            ["cpu"] = Curve(P65Golden.CoolCpuCurve),
            ["gpu"] = Curve(P65Golden.CoolGpuCurve),
            ["vrm"] = Curve(P65Golden.CoolCpuCurve),
        });

        Rejects(P65() with { Presets = [preset] }, "*\"vrm\" diye bir fan yok*");
    }

    [Fact]
    public void Rejects_a_preset_that_leaves_out_a_fan()
    {
        var preset = new PresetConfig("Half", new Dictionary<string, FanCurve>
        {
            ["cpu"] = Curve(P65Golden.CoolCpuCurve),
        });

        Rejects(P65() with { Presets = [preset] }, "*Half: gpu fanının eğrisi yok*");
    }

    [Fact]
    public void Rejects_duplicate_preset_names()
    {
        var cool = P65().Presets[0];

        Rejects(P65() with { Presets = [cool, cool] }, "*preset \"Cool\" iki kez*");
    }

    [Fact]
    public void Rejects_presets_when_a_fan_has_no_down_offsets()
    {
        var cpu = CpuFan() with { FactoryDownOffsets = null, FactoryCurve = null };

        Rejects(P65() with { Status = DeviceStatus.Draft, Fans = [cpu, GpuFan()] }, "*Cool: cpu fanının iniş ofsetleri yok*");
    }

    [Fact]
    public void Rejects_write_verified_without_an_exact_firmware()
    {
        Rejects(P65() with { Firmware = new FirmwareSpec([], ["16Q4EMS2.1"]) }, "*writeVerified*");
    }

    [Fact]
    public void Rejects_write_verified_when_a_fan_lacks_factory_data()
    {
        var gpu = GpuFan() with { FactoryDownOffsets = null, FactoryCurve = null };

        Rejects(P65() with { Presets = [], Fans = [CpuFan(), gpu] }, "*writeVerified*");
    }

    [Theory]
    [InlineData("16Q4EMS2.10")]
    [InlineData("16Q4EMS2.1077")]
    [InlineData("16Q4EMS1.107")]
    public void Rejects_an_exact_firmware_of_the_wrong_length_or_family(string firmware)
    {
        Rejects(P65() with { Firmware = new FirmwareSpec([firmware], ["16Q4EMS2.1"]) }, $"*{firmware}*");
    }

    [Fact]
    public void Rejects_a_firmware_listed_twice()
    {
        Rejects(P65() with { Firmware = new FirmwareSpec([P65Golden.Firmware, P65Golden.Firmware], ["16Q4EMS2.1"]) },
            $"*{P65Golden.Firmware}*");
    }

    [Theory]
    [InlineData("16Q4EMS2.")]
    [InlineData("16Q4EMS2.10")]
    public void Rejects_a_family_that_is_not_ten_characters(string family)
    {
        Rejects(P65() with { Firmware = new FirmwareSpec([], [family]), Status = DeviceStatus.Draft, Presets = [] }, "*families*");
    }

    [Fact]
    public void Rejects_a_port_register_from_a_fan_table()
    {
        Rejects(P65() with { PortRegisters = [0x98, 0x6A] }, "*0x6A fan tablosu*");
    }

    [Fact]
    public void Rejects_a_port_register_outside_the_code_ceiling()
    {
        Rejects(P65() with { PortRegisters = [0x98, 0x99] }, "*0x99 port tavanında değil*");
    }

    [Fact]
    public void Rejects_a_port_register_listed_twice()
    {
        Rejects(P65() with { PortRegisters = [0x98, 0x98] }, "*0x98 iki kez*");
    }

    [Fact]
    public void Rejects_a_port_register_that_also_has_a_wmi_field()
    {
        var wmi = new Wmi1Layout([.. P65().Wmi1!.Fields, new WmiFieldSpec(0x98, "MSI_System", 3)]);

        Rejects(P65() with { Wmi1 = wmi }, "*0x98 hem portta*");
    }

    [Fact]
    public void The_port_ceiling_is_cooler_boost_and_the_charge_limit_only()
    {
        DeviceConfigValidator.PortCeiling.Order().Should().Equal(0x98, 0xEF);
    }

    [Fact]
    public void Rejects_the_wmi2_charge_register_on_the_port()
    {
        var features = P65().Features with { ChargeLimit = new ChargeLimitFeature(0xD7, 7, 50, 100) };

        Rejects(P65() with { Features = features, PortRegisters = [0x98, 0xD7] }, "*0xD7 port tavanında değil*");
    }

    [Fact]
    public void Rejects_a_wmi1_config_without_a_wmi1_layout()
    {
        Rejects(P65() with { Wmi1 = null }, "*wmi1*");
    }

    [Fact]
    public void Rejects_a_wmi_field_register_listed_twice()
    {
        var wmi = new Wmi1Layout([.. P65().Wmi1!.Fields, new WmiFieldSpec(0x68, "MSI_CPU", 3)]);

        Rejects(P65() with { Wmi1 = wmi }, "*0x68 için iki WMI*");
    }

    [Theory]
    [InlineData("MSI_CPU", -1)]
    [InlineData("Win32_Process", 1)]
    [InlineData("", 1)]
    public void Rejects_a_wmi_field_with_a_bad_class_or_index(string className, int index)
    {
        var wmi = new Wmi1Layout([.. P65().Wmi1!.Fields, new WmiFieldSpec(0x99, className, index)]);

        Rejects(P65() with { Wmi1 = wmi }, "*0x99: WMI alanı*");
    }

    [Fact]
    public void Rejects_duplicate_performance_mode_values()
    {
        var modes = new PerformanceModeFeature(0xF2, [new ModeValue("high", 0xC0), new ModeValue("turbo", 0xC0)]);

        Rejects(P65() with { Features = P65().Features with { PerformanceMode = modes } }, "*değeri 0xC0 iki kez*");
    }

    [Fact]
    public void Rejects_duplicate_performance_mode_ids()
    {
        var modes = new PerformanceModeFeature(0xF2, [new ModeValue("high", 0xC0), new ModeValue("high", 0xC4)]);

        Rejects(P65() with { Features = P65().Features with { PerformanceMode = modes } }, "*id \"high\" iki kez*");
    }

    [Fact]
    public void Rejects_an_empty_performance_mode_list()
    {
        var modes = new PerformanceModeFeature(0xF2, []);

        Rejects(P65() with { Features = P65().Features with { PerformanceMode = modes } }, "*performanceMode*");
    }

    [Fact]
    public void Rejects_a_fan_mode_whose_auto_and_advanced_values_are_equal()
    {
        var fanMode = new FanModeFeature(0xF4, 0x8D, 0x8D);

        Rejects(P65() with { Features = P65().Features with { FanMode = fanMode } }, "*fanMode*");
    }

    [Theory]
    [InlineData(7, 50, 101)]
    [InlineData(7, 80, 60)]
    [InlineData(7, 49, 100)]
    [InlineData(7, -1, 100)]
    [InlineData(6, 50, 100)]
    public void Rejects_a_charge_limit_beyond_the_code_ceiling(int enableBit, int min, int max)
    {
        var charge = new ChargeLimitFeature(0xEF, enableBit, min, max);

        Rejects(P65() with { Features = P65().Features with { ChargeLimit = charge } }, "*chargeLimit*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void Rejects_a_cooler_boost_bit_outside_the_byte(int bit)
    {
        var boost = new CoolerBoostFeature(0x98, bit);

        Rejects(P65() with { Features = P65().Features with { CoolerBoost = boost } }, "*coolerBoost*");
    }

    [Theory]
    [InlineData(0xA0, 0, 0xAC, 8)]
    [InlineData(0xA0, 12, 0xFC, 8)]
    public void Rejects_a_firmware_location_that_is_empty_or_runs_past_0xFF(int version, int versionLength, int date, int dateLength)
    {
        var location = new FirmwareLocation((byte)version, versionLength, (byte)date, dateLength);

        Rejects(P65() with { FirmwareLocation = location }, "*firmwareLocation*");
    }

    [Fact]
    public void Rejects_a_fan_register_inside_the_firmware_string()
    {
        var gpu = GpuFan() with { Temperature = 0xA4 };

        Rejects(P65() with { Fans = [CpuFan(), gpu] }, "*0xA4 birden çok yerde*");
    }

    [Fact]
    public void Rejects_a_fan_id_that_is_not_lowercase_kebab_case()
    {
        Rejects(P65() with { Fans = [CpuFan(), GpuFan() with { Id = "GPU" }], Presets = [] }, "*GPU*");
    }

    [Fact]
    public void Rejects_an_rpm_register_at_0xFF()
    {
        Rejects(P65() with { Fans = [CpuFan(), GpuFan() with { RpmHigh = 0xFF }] }, "*RPM*");
    }

    [Fact]
    public void Rejects_negative_down_offsets()
    {
        var cpu = CpuFan() with { FactoryDownOffsets = [8, 3, 3, -1, 3, 3] };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*cpu: iniş ofsetleri*");
    }

    [Fact]
    public void Rejects_a_factory_curve_without_down_offsets()
    {
        var cpu = CpuFan() with { FactoryDownOffsets = null };

        Rejects(P65() with { Status = DeviceStatus.Draft, Presets = [], Fans = [cpu, GpuFan()] }, "*cpu: fabrika eğrisi iniş ofsetleri olmadan*");
    }

    [Fact]
    public void Rejects_a_performance_mode_id_that_is_not_lowercase_kebab_case()
    {
        var modes = new PerformanceModeFeature(0xF2, [new ModeValue("High Perf", 0xC0)]);

        Rejects(P65() with { Features = P65().Features with { PerformanceMode = modes } }, "*High Perf*");
    }

    [Fact]
    public void Rejects_a_blank_preset_name()
    {
        var preset = P65().Presets[0] with { Name = "" };

        Rejects(P65() with { Presets = [preset] }, "*preset adı boş*");
    }

    [Fact]
    public void Accepts_a_model_without_optional_features()
    {
        var features = new FeatureSet(CoolerBoost: null, ChargeLimit: null, PerformanceMode: null, FanMode: null);

        DeviceConfigValidator.Validate(Draft() with { Features = features, PortRegisters = [] }).Should().BeEmpty();
    }

    [Fact]
    public void Accepts_a_wmi2_model_without_a_wmi1_layout()
    {
        DeviceConfigValidator.Validate(Draft() with { Interface = WmiInterface.Wmi2, Wmi1 = null }).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_null_elements_left_by_json_reading_without_throwing()
    {
        var config = P65() with { Fans = [CpuFan(), null!] };

        DeviceConfigValidator.Validate(config).Should().ContainSingle().Which.Should().Contain("null");
    }

    [Theory]
    [InlineData("abc\n")]
    [InlineData("abc\r\n")]
    public void Rejects_an_id_with_a_trailing_line_break(string id)
    {
        Rejects(P65() with { Id = id }, "*id*");
    }

    [Fact]
    public void Rejects_a_wmi_class_with_a_trailing_line_break()
    {
        var wmi = new Wmi1Layout([.. P65().Wmi1!.Fields, new WmiFieldSpec(0x99, "MSI_CPU\n", 30)]);

        Rejects(P65() with { Wmi1 = wmi }, "*0x99: WMI alanı*");
    }

    [Fact]
    public void Rejects_two_registers_on_the_same_wmi_field()
    {
        var wmi = new Wmi1Layout([.. P65().Wmi1!.Fields, new WmiFieldSpec(0x99, "MSI_CPU", 1)]);

        Rejects(P65() with { Wmi1 = wmi }, "*MSI_CPU[1] birden çok register*");
    }

    [Theory]
    [InlineData(0x68)]
    [InlineData(0x6A)]
    [InlineData(0xCD)]
    [InlineData(0xF2)]
    [InlineData(0xA5)]
    public void Rejects_a_wmi1_register_with_no_wmi_field_and_no_port_route(int register)
    {
        var wmi = new Wmi1Layout([.. P65().Wmi1!.Fields.Where(f => f.Register != register)]);

        Rejects(P65() with { Wmi1 = wmi }, $"*0x{register:X2} için okuma yolu yok*");
    }

    [Fact]
    public void Rejects_write_verified_on_a_firmware_outside_the_code_allow_list()
    {
        Rejects(P65() with { Firmware = new FirmwareSpec(["16Q4EMS2.108"], ["16Q4EMS2.1"]) }, "*16Q4EMS2.108*kodda*");
    }

    [Fact]
    public void Rejects_write_verified_on_wmi2()
    {
        Rejects(P65() with { Interface = WmiInterface.Wmi2 }, "*writeVerified*wmi1*");
    }

    [Fact]
    public void Rejects_a_record_without_sources()
    {
        Rejects(P65() with { Sources = [] }, "*sources*");
    }

    [Theory]
    [InlineData("P65\u001b[31m")]
    [InlineData("P65\nfake log line")]
    public void Rejects_control_characters_in_free_text(string text)
    {
        Rejects(P65() with { DisplayName = text }, "*displayName*kontrol karakteri*");
        Rejects(P65() with { Sources = [text] }, "*sources*kontrol karakteri*");
        Rejects(P65() with { Presets = [P65().Presets[0] with { Name = text }] }, "*preset adı*kontrol karakteri*");
        Rejects(P65() with { Firmware = new FirmwareSpec([text], ["16Q4EMS2.1"]) }, "*firmware*kontrol karakteri*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void Rejects_down_offsets_outside_one_to_fifteen(int offset)
    {
        var cpu = CpuFan() with { FactoryDownOffsets = [offset, 3, 3, 3, 3, 3] };

        Rejects(P65() with { Fans = [cpu, GpuFan()] }, "*cpu: iniş ofsetleri*");
    }

    [Fact]
    public void Rejects_cooler_boost_and_charge_limit_swapping_port_registers()
    {
        var features = P65().Features with
        {
            CoolerBoost = new CoolerBoostFeature(0xEF, 7),
            ChargeLimit = new ChargeLimitFeature(0x98, 7, 50, 100),
        };

        var errors = DeviceConfigValidator.Validate(P65() with { Features = features });

        errors.Should().ContainMatch("*coolerBoost*0x98*").And.ContainMatch("*chargeLimit*0xEF*");
    }

    [Fact]
    public void Rejects_a_port_register_that_belongs_to_no_feature()
    {
        var features = P65().Features with { ChargeLimit = null };

        Rejects(P65() with { Features = features }, "*0xEF hiçbir özelliğe ait değil*");
    }

    [Fact]
    public void Catalog_rejects_records_that_share_a_family()
    {
        var other = Draft() with { Id = "other-draft" };

        DeviceConfigValidator.FindConflicts([Draft(), other])
            .Select(c => c.Id).Should().BeEquivalentTo("draft-test", "other-draft");
    }

    [Fact]
    public void Catalog_rejects_both_records_that_claim_the_same_firmware()
    {
        var other = P65() with { Id = "msi-p65-copy" };

        DeviceConfigValidator.FindConflicts([P65(), other, Draft()])
            .Select(c => c.Id).Should().BeEquivalentTo("msi-p65-creator-9se", "msi-p65-copy");
    }

    [Fact]
    public void Catalog_rejects_records_with_the_same_id()
    {
        var conflicts = DeviceConfigValidator.FindConflicts([Draft(), Draft() with { DisplayName = "Other" }]);

        conflicts.Should().ContainSingle().Which.Id.Should().Be("draft-test");
    }

    [Fact]
    public void Catalog_without_shared_firmware_or_ids_has_no_conflicts()
    {
        DeviceConfigValidator.FindConflicts([P65(), Draft()]).Should().BeEmpty();
    }

    private static void Rejects(DeviceConfig config, string pattern) =>
        DeviceConfigValidator.Validate(config).Should().ContainMatch(pattern);
}
