using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device;

public class DeviceCapabilitiesTests
{
    private static DeviceConfig WithModes(params ModeValue[] modes) => P65() with
    {
        Features = P65().Features with { PerformanceMode = new PerformanceModeFeature(0xF2, modes) },
    };

    [Fact]
    public void The_P65_has_every_control()
    {
        DeviceCapabilities.From(P65()).Should().BeEquivalentTo(new
        {
            GpuFan = true,
            FanCurve = true,
            CoolerBoost = true,
            ChargeLimit = true,
            PerformanceModes = new[] { PerformanceMode.High, PerformanceMode.Balanced, PerformanceMode.Eco },
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void The_embedded_P65_layout_carries_the_same_capabilities()
    {
        TestLayouts.P65.Capabilities.Should().BeEquivalentTo(DeviceCapabilities.From(TestLayouts.P65Config), o => o.WithStrictOrdering());
    }

    [Fact]
    public void A_single_fan_model_has_no_gpu_fan()
    {
        DeviceCapabilities.From(P65() with { Fans = [CpuFan()] }).GpuFan.Should().BeFalse();
    }

    [Fact]
    public void A_model_without_a_charge_limit_has_none()
    {
        DeviceCapabilities.From(P65() with { Features = P65().Features with { ChargeLimit = null } }).ChargeLimit.Should().BeFalse();
    }

    [Fact]
    public void A_model_without_cooler_boost_has_none()
    {
        DeviceCapabilities.From(P65() with { Features = P65().Features with { CoolerBoost = null } }).CoolerBoost.Should().BeFalse();
    }

    [Fact]
    public void Fan_curves_need_the_fan_mode_switch()
    {
        DeviceCapabilities.From(P65() with { Features = P65().Features with { FanMode = null } }).FanCurve.Should().BeFalse();
    }

    [Fact]
    public void Performance_modes_keep_the_record_order()
    {
        DeviceCapabilities.From(WithModes(new ModeValue("eco", 0xC2), new ModeValue("high", 0xC0))).PerformanceModes
            .Should().Equal(PerformanceMode.Eco, PerformanceMode.High);
    }

    [Fact]
    public void A_mode_M_Tool_cannot_name_yet_is_left_out()
    {
        DeviceCapabilities.From(WithModes(new ModeValue("turbo", 0xC4), new ModeValue("balanced", 0xC1))).PerformanceModes
            .Should().Equal(PerformanceMode.Balanced);
    }

    [Fact]
    public void A_model_without_performance_modes_lists_none()
    {
        DeviceCapabilities.From(P65() with { Features = P65().Features with { PerformanceMode = null } }).PerformanceModes
            .Should().BeEmpty();
    }

    [Fact]
    public void A_desired_state_the_device_supports_stays_as_it_is()
    {
        var desired = new DesiredState("Cool", PerformanceMode.Eco, ChargeLimitPercent: 80);
        var warnings = new List<string>();

        DeviceCapabilities.From(P65()).Restrict(desired, warnings).Should().Be(desired);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void A_desired_mode_the_device_lacks_is_dropped_with_a_warning()
    {
        var caps = DeviceCapabilities.From(WithModes(new ModeValue("balanced", 0xC1)));
        var warnings = new List<string>();

        var restricted = caps.Restrict(new DesiredState("Cool", PerformanceMode.High, ChargeLimitPercent: 80), warnings);

        restricted.Should().Be(new DesiredState("Cool", Performance: null, ChargeLimitPercent: 80));
        restricted.ToPlans(ProfileCatalog.BuiltIn).Should().NotContain(p => p.Description.Contains("Performans"));
        warnings.Should().ContainSingle().Which.Should().Contain("Yüksek");
    }

    [Fact]
    public void A_desired_charge_limit_on_a_model_without_one_is_dropped_with_a_warning()
    {
        var caps = DeviceCapabilities.From(P65() with { Features = P65().Features with { ChargeLimit = null } });
        var warnings = new List<string>();

        caps.Restrict(new DesiredState("Cool", PerformanceMode.Eco, ChargeLimitPercent: 80), warnings)
            .Should().Be(new DesiredState("Cool", PerformanceMode.Eco, ChargeLimitPercent: null));
        warnings.Should().ContainSingle().Which.Should().Contain("şarj");
    }
}
