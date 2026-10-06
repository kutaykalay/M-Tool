using MTool.Core.Device;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;

namespace MTool.Tests.Power;

public class PowerSwitchRulesTests
{
    private static readonly DesiredState CoolHigh = new("Cool", PerformanceMode.High, ChargeLimitPercent: 80, FanMode: FanMode.Auto);

    private static AppSettings On(PowerProfilePair? ac = null, PowerProfilePair? battery = null, DesiredState? desired = null) =>
        AppSettings.Default with { Desired = desired ?? CoolHigh, PowerSwitch = new PowerSwitchSettings(true, ac, battery) };

    [Fact]
    public void Align_takes_the_fan_profile_and_performance_from_the_pair_and_keeps_the_rest()
    {
        var settings = On(battery: new PowerProfilePair("Silent", PerformanceMode.Eco));

        var aligned = PowerSwitchRules.Align(settings, PowerSource.Battery);

        aligned.Desired.Should().Be(new DesiredState("Silent", PerformanceMode.Eco, ChargeLimitPercent: 80, FanMode: FanMode.Auto));
        aligned.PowerSwitch.Should().Be(settings.PowerSwitch);
    }

    [Fact]
    public void Align_leaves_the_performance_alone_when_the_pair_does_not_name_one()
    {
        var aligned = PowerSwitchRules.Align(On(ac: new PowerProfilePair("Silent")), PowerSource.Ac);

        aligned.Desired.Should().Be(CoolHigh with { FanProfile = "Silent" });
    }

    [Fact]
    public void Align_without_a_pair_keeps_the_desired_state_and_remembers_it_as_the_pair()
    {
        var settings = On(ac: new PowerProfilePair("Silent"));

        var aligned = PowerSwitchRules.Align(settings, PowerSource.Battery);

        aligned.Desired.Should().Be(CoolHigh);
        aligned.PowerSwitch.Battery.Should().Be(new PowerProfilePair("Cool", PerformanceMode.High));
        aligned.PowerSwitch.Ac.Should().Be(new PowerProfilePair("Silent"));
    }

    [Fact]
    public void Align_does_nothing_while_switching_is_off()
    {
        var settings = On(battery: new PowerProfilePair("Silent")) with
        {
            PowerSwitch = new PowerSwitchSettings(false, Battery: new PowerProfilePair("Silent")),
        };

        PowerSwitchRules.Align(settings, PowerSource.Battery).Should().BeSameAs(settings);
    }

    [Fact]
    public void Align_does_not_change_its_input()
    {
        var settings = On(battery: new PowerProfilePair("Silent", PerformanceMode.Eco));
        var before = settings with { };

        PowerSwitchRules.Align(settings, PowerSource.Battery);

        settings.Should().Be(before);
    }

    [Fact]
    public void Record_updates_only_the_pair_of_the_given_source()
    {
        var settings = On(ac: new PowerProfilePair("Default", PerformanceMode.Balanced), battery: new PowerProfilePair("Silent"));

        var recorded = PowerSwitchRules.Record(settings, PowerSource.Ac);

        recorded.PowerSwitch.Ac.Should().Be(new PowerProfilePair("Cool", PerformanceMode.High));
        recorded.PowerSwitch.Battery.Should().Be(new PowerProfilePair("Silent"));
        recorded.Desired.Should().Be(CoolHigh);
    }

    [Fact]
    public void Record_does_nothing_while_switching_is_off()
    {
        var settings = AppSettings.Default with { Desired = CoolHigh };

        PowerSwitchRules.Record(settings, PowerSource.Battery).Should().BeSameAs(settings);
    }

    [Fact]
    public void Enable_remembers_the_current_choice_for_the_current_source_and_keeps_the_other_pair()
    {
        var settings = AppSettings.Default with
        {
            Desired = CoolHigh,
            PowerSwitch = new PowerSwitchSettings(false, Ac: new PowerProfilePair("Default"), Battery: new PowerProfilePair("Silent")),
        };

        var enabled = PowerSwitchRules.Enable(settings, true, PowerSource.Ac);

        enabled.PowerSwitch.Should().Be(new PowerSwitchSettings(
            true, new PowerProfilePair("Cool", PerformanceMode.High), new PowerProfilePair("Silent")));
        enabled.Desired.Should().Be(CoolHigh);
    }

    [Fact]
    public void Enable_without_a_known_source_only_sets_the_flag()
    {
        var enabled = PowerSwitchRules.Enable(AppSettings.Default, true, source: null);

        enabled.PowerSwitch.Should().Be(new PowerSwitchSettings(Enabled: true));
    }

    [Fact]
    public void Disable_keeps_the_pairs_for_later()
    {
        var settings = On(new PowerProfilePair("Cool"), new PowerProfilePair("Silent"));

        var disabled = PowerSwitchRules.Enable(settings, false, PowerSource.Battery);

        disabled.PowerSwitch.Should().Be(settings.PowerSwitch with { Enabled = false });
        disabled.Desired.Should().Be(settings.Desired);
    }
}
