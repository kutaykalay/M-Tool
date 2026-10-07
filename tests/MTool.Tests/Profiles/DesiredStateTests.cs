using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Tests.Profiles;

public class DesiredStateTests
{
    private static readonly ProfileCatalog Catalog = ProfileCatalog.BuiltIn;

    private static readonly ControlState FactoryState = new(
        FanCurves: FactoryDefaults.FanCurves,
        Performance: null,
        PerformanceRaw: 0x80,
        FanMode: FanMode.Advanced,
        Port: new PortState(CoolerBoostRaw: 0x02, ChargeLimitRaw: 0xD0));

    [Fact]
    public void First_run_default_is_default_fan_table_balanced_and_full_charge()
    {
        var plans = DesiredState.Default.ToPlans(Catalog);

        plans.Should().BeEquivalentTo(
            [
                WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default"),
                WritePlans.Performance(PerformanceMode.Balanced),
                WritePlans.ChargeLimit(100),
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_state_without_choices_wants_only_its_fan_table()
    {
        var plans = new DesiredState("Default").ToPlans(Catalog);

        plans.Should().ContainSingle().Which.Should().BeEquivalentTo(WritePlans.FanCurves(FactoryDefaults.FanCurves, "Default"));
    }

    [Fact]
    public void Plans_come_in_fan_table_performance_fan_mode_order_with_the_port_plan_last()
    {
        var desired = new DesiredState("Cool", PerformanceMode.Balanced, 60, FanMode.Auto);

        var plans = desired.ToPlans(Catalog);

        plans.Should().BeEquivalentTo(
            [
                WritePlans.FanCurves(Presets.Cool.Curves, "Cool"),
                WritePlans.Performance(PerformanceMode.Balanced),
                WritePlans.Fan(FanMode.Auto),
                WritePlans.ChargeLimit(60),
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Unset_fields_are_skipped()
    {
        var desired = new DesiredState("Silent", Performance: null, ChargeLimitPercent: 90, FanMode: null);

        desired.ToPlans(Catalog).Select(p => p.Description)
            .Should().Equal("Fan profile: Silent", "Charge limit: 90%");
    }

    [Fact]
    public void Unknown_profile_cannot_be_planned()
    {
        var act = () => new DesiredState("Turbo").ToPlans(Catalog);

        act.Should().Throw<ArgumentException>().WithMessage("*Turbo*");
    }

    [Fact]
    public void Ec_in_the_wanted_state_has_no_drift()
    {
        var desired = new DesiredState("Default", ChargeLimitPercent: 80, FanMode: FanMode.Advanced);

        desired.DriftFrom(FactoryState, Catalog).Any.Should().BeFalse();
    }

    [Fact]
    public void Factory_tables_after_a_reboot_drift_from_cool()
    {
        var drift = new DesiredState("Cool").DriftFrom(FactoryState, Catalog);

        drift.Should().Be(new StateDrift(FanTable: true, Performance: false, ChargeLimit: false, FanMode: false));
        drift.Any.Should().BeTrue();
    }

    [Fact]
    public void Undefined_performance_byte_drifts_from_a_wanted_mode()
    {
        var drift = new DesiredState("Default", PerformanceMode.High).DriftFrom(FactoryState, Catalog);

        drift.Performance.Should().BeTrue();
    }

    [Fact]
    public void Charge_limit_and_fan_mode_drift_is_reported_separately()
    {
        var desired = new DesiredState("Default", ChargeLimitPercent: 60, FanMode: FanMode.Auto);

        var drift = desired.DriftFrom(FactoryState, Catalog);

        drift.Should().Be(new StateDrift(FanTable: false, Performance: false, ChargeLimit: true, FanMode: true));
    }

    [Fact]
    public void A_wanted_charge_limit_that_is_not_known_counts_as_drift()
    {
        var desired = new DesiredState("Default", ChargeLimitPercent: 80);

        desired.DriftFrom(FactoryState with { Port = null }, Catalog).ChargeLimit.Should().BeTrue();
    }

    [Fact]
    public void An_unknown_port_state_is_no_drift_when_no_charge_limit_is_wanted()
    {
        new DesiredState("Default").DriftFrom(FactoryState with { Port = null }, Catalog).Any.Should().BeFalse();
    }

    [Fact]
    public void Unknown_profile_counts_as_fan_table_drift()
    {
        new DesiredState("Turbo").DriftFrom(FactoryState, Catalog).FanTable.Should().BeTrue();
    }

    [Fact]
    public void Without_port_parts_drops_only_the_charge_limit()
    {
        var desired = new DesiredState("Cool", PerformanceMode.Balanced, 60, FanMode.Auto);

        var wmiOnly = desired.WithoutPortParts();

        wmiOnly.Should().Be(new DesiredState("Cool", PerformanceMode.Balanced, ChargeLimitPercent: null, FanMode.Auto));
        wmiOnly.ToPlans(Catalog).Should().NotContain(p => p.Writes.Any(w => w.Register == EcMap.ChargeLimit));
    }
}
