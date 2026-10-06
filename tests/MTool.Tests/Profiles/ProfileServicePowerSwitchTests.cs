using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Profiles;

/// <summary>Separate choices on AC and on battery, through <see cref="ProfileService"/>.</summary>
public sealed class ProfileServicePowerSwitchTests : IDisposable
{
    private static readonly DesiredState CoolHigh = new("Cool", PerformanceMode.High, ChargeLimitPercent: 80);
    private static readonly PowerProfilePair SilentEco = new("Silent", PerformanceMode.Eco);

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly ListLog _log = new();
    private readonly List<DesiredState> _changes = [];

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static AppSettings On(PowerProfilePair? ac = null, PowerProfilePair? battery = null) =>
        AppSettings.Default with { Desired = CoolHigh, PowerSwitch = new PowerSwitchSettings(true, ac, battery) };

    private ProfileService Service(AppSettings settings)
    {
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), settings, _log);
        service.DesiredChanged += _changes.Add;
        return service;
    }

    /// <summary>A service that already knows it is on AC; the start-up alignment wrote nothing.</summary>
    private async Task<ProfileService> OnAc(AppSettings settings)
    {
        var service = Service(settings);
        await service.SwitchPowerSourceAsync(PowerSource.Ac, write: false);
        _changes.Clear();
        return service;
    }

    private AppSettings Saved() => new SettingsStore(_folder).Load().Settings;

    private bool SettingsFileExists => File.Exists(Path.Combine(_folder, "settings.json"));

    [Fact]
    public async Task While_switching_is_off_a_new_source_changes_nothing()
    {
        var service = Service(AppSettings.Default with
        {
            Desired = CoolHigh,
            PowerSwitch = new PowerSwitchSettings(false, Battery: SilentEco),
        });

        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        outcomes.Should().BeNull();
        _control.Calls.Should().BeEmpty();
        service.Desired.Should().Be(CoolHigh);
        SettingsFileExists.Should().BeFalse();
    }

    [Fact]
    public async Task A_new_source_writes_its_pair_without_the_port_and_saves_it()
    {
        var service = await OnAc(On(battery: SilentEco));

        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        var expected = new DesiredState("Silent", PerformanceMode.Eco, ChargeLimitPercent: 80);
        outcomes.Should().ContainSingle().Which.Status.Should().Be(WriteStatus.Applied);
        _control.Calls.Should().Equal($"desired {expected with { ChargeLimitPercent = null }}");
        service.Desired.Should().Be(expected);
        Saved().Desired.Should().Be(expected);
        _changes.Should().Equal(expected);
    }

    [Fact]
    public async Task The_same_source_again_does_nothing()
    {
        var service = await OnAc(On(battery: SilentEco));
        await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);
        // Make the pair differ from the desired state, so only the same-source rule can stop a write.
        await service.SetPowerSwitchAsync(false);
        await service.SelectProfileAsync("Default");
        await service.SetPowerSwitchAsync(true);
        _control.Calls.Clear();
        _changes.Clear();

        var again = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        again.Should().BeNull();
        _control.Calls.Should().BeEmpty();
        _changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Choices_by_hand_come_back_when_the_cable_goes_out_and_in_again()
    {
        var service = await OnAc(On());
        await service.SelectProfileAsync("Silent");
        await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);
        await service.SetPerformanceAsync(PerformanceMode.Eco);
        await service.SelectProfileAsync("Default");

        await service.SwitchPowerSourceAsync(PowerSource.Ac, write: true);
        service.Desired.Should().Be(new DesiredState("Silent", PerformanceMode.High, ChargeLimitPercent: 80));

        await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);
        service.Desired.Should().Be(new DesiredState("Default", PerformanceMode.Eco, ChargeLimitPercent: 80));
    }

    [Fact]
    public async Task A_switch_raises_desired_changed_once_after_the_turn_ends()
    {
        var service = await OnAc(On(battery: SilentEco));
        var raised = new List<DesiredState>();
        Task? reentered = null;
        var calledBack = false;
        service.DesiredChanged += desired =>
        {
            raised.Add(desired);
            // The guard is set before the call: the call raises this event again before it returns.
            if (calledBack)
            {
                return;
            }

            calledBack = true;
            reentered = service.SetChargeLimitAsync(70);
        };

        await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        var finished = await Task.WhenAny(reentered!, Task.Delay(TimeSpan.FromSeconds(5)));
        finished.Should().BeSameAs(reentered, "a subscriber may call back into the service once the turn is free");
        // One event from the switch, then one from the charge limit set by the subscriber.
        raised.Select(d => (d.FanProfile, d.ChargeLimitPercent)).Should().Equal(("Silent", 80), ("Silent", 70));
    }

    [Fact]
    public async Task After_the_battery_profile_is_deleted_switching_to_battery_keeps_what_is_set()
    {
        var gece = new FanProfile("Gece", Presets.Silent.Curves);
        var service = await OnAc(On(battery: new PowerProfilePair("Gece")) with { CustomProfiles = [gece] });

        (await service.DeleteProfileAsync("Gece")).Error.Should().BeNull();
        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        outcomes.Should().BeNull();
        service.Desired.FanProfile.Should().Be("Cool");
    }

    [Fact]
    public async Task Without_write_the_desired_state_follows_but_the_ec_is_not_touched()
    {
        var service = await OnAc(On(battery: SilentEco));
        var busySeen = false;
        service.DesiredChanged += _ => busySeen |= service.IsBusy;

        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: false);

        outcomes.Should().BeNull();
        _control.Calls.Should().BeEmpty();
        service.Desired.FanProfile.Should().Be("Silent");
        Saved().Desired.FanProfile.Should().Be("Silent");
        busySeen.Should().BeFalse();
        service.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task A_source_without_a_pair_keeps_what_is_set_and_remembers_it()
    {
        var service = await OnAc(On());

        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        outcomes.Should().BeNull();
        _control.Calls.Should().BeEmpty();
        service.Desired.Should().Be(CoolHigh);
        Saved().PowerSwitch.Battery.Should().Be(new PowerProfilePair("Cool", PerformanceMode.High));
    }

    [Fact]
    public async Task Equal_pairs_write_nothing_when_the_source_changes()
    {
        var pair = new PowerProfilePair("Cool", PerformanceMode.High);
        var service = await OnAc(On(pair, pair));

        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        outcomes.Should().BeNull();
        _control.Calls.Should().BeEmpty();
        _changes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WriteStatus.Applied)]
    [InlineData(WriteStatus.DryRun)]
    public async Task A_choice_by_hand_becomes_the_pair_of_the_current_source(WriteStatus status)
    {
        var service = await OnAc(On(ac: new PowerProfilePair("Cool", PerformanceMode.High), battery: SilentEco));
        await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);
        _control.NextStatus = status;

        await service.SelectProfileAsync("Default");
        await service.SetPerformanceAsync(PerformanceMode.Balanced);

        var saved = Saved().PowerSwitch;
        saved.Battery.Should().Be(new PowerProfilePair("Default", PerformanceMode.Balanced));
        saved.Ac.Should().Be(new PowerProfilePair("Cool", PerformanceMode.High));
    }

    [Fact]
    public async Task A_rejected_choice_by_hand_leaves_the_pair_alone()
    {
        var service = await OnAc(On(battery: SilentEco));
        await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);
        _control.NextStatus = WriteStatus.Rejected;

        await service.SelectProfileAsync("Default");

        Saved().PowerSwitch.Battery.Should().Be(SilentEco);
    }

    [Fact]
    public async Task Charge_limit_and_cooler_boost_leave_the_pairs_alone()
    {
        var service = await OnAc(On(ac: new PowerProfilePair("Cool"), battery: SilentEco));

        await service.SetChargeLimitAsync(60);
        await service.SetCoolerBoostAsync(true);

        service.PowerSwitch.Should().Be(new PowerSwitchSettings(true, new PowerProfilePair("Cool"), SilentEco));
    }

    [Fact]
    public async Task A_choice_by_hand_before_the_source_is_known_leaves_the_pairs_alone()
    {
        var service = Service(On(battery: SilentEco));

        await service.SelectProfileAsync("Default");

        service.PowerSwitch.Should().Be(new PowerSwitchSettings(true, Battery: SilentEco));
    }

    [Fact]
    public async Task Turning_switching_on_remembers_the_current_choice_and_writes_nothing()
    {
        var service = Service(AppSettings.Default with { Desired = CoolHigh });
        await service.SwitchPowerSourceAsync(PowerSource.Battery, write: false);

        var result = await service.SetPowerSwitchAsync(true);

        result.Error.Should().BeNull();
        _control.Calls.Should().BeEmpty();
        Saved().PowerSwitch.Should().Be(new PowerSwitchSettings(true, Battery: new PowerProfilePair("Cool", PerformanceMode.High)));
        service.Desired.Should().Be(CoolHigh);
    }

    [Fact]
    public async Task Turning_switching_off_keeps_the_pairs_in_the_file()
    {
        var service = await OnAc(On(new PowerProfilePair("Cool", PerformanceMode.High), SilentEco));

        await service.SetPowerSwitchAsync(false);

        Saved().PowerSwitch.Should().Be(new PowerSwitchSettings(false, new PowerProfilePair("Cool", PerformanceMode.High), SilentEco));
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_switch_waits_for_a_running_command_and_then_writes_the_newest_state()
    {
        var service = await OnAc(On(battery: SilentEco));
        _control.WriteGate = new TaskCompletionSource();

        var command = service.SetChargeLimitAsync(70);
        SpinWait.SpinUntil(() => !_control.Calls.IsEmpty, TimeSpan.FromSeconds(5)).Should().BeTrue("the command's write started");
        var switching = service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        switching.IsCompleted.Should().BeFalse();
        _control.Calls.Should().Equal("charge 70");

        _control.WriteGate.SetResult();
        await Task.WhenAll(command, switching);

        _control.Calls.Should().Equal("charge 70", $"desired {new DesiredState("Silent", PerformanceMode.Eco)}");
        _control.MaxWritesRunning.Should().Be(1);
        service.Desired.Should().Be(new DesiredState("Silent", PerformanceMode.Eco, ChargeLimitPercent: 70));
    }

    [Fact]
    public async Task A_writing_switch_counts_as_busy()
    {
        var service = await OnAc(On(battery: SilentEco));
        _control.WriteGate = new TaskCompletionSource();

        var switching = service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);
        SpinWait.SpinUntil(() => !_control.Calls.IsEmpty, TimeSpan.FromSeconds(5)).Should().BeTrue("the switch's write started");

        service.IsBusy.Should().BeTrue();
        _control.WriteGate.SetResult();
        await switching;
        service.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task A_rejected_switch_write_keeps_the_new_desired_state()
    {
        var service = await OnAc(On(battery: SilentEco));
        _control.NextStatus = WriteStatus.Rejected;

        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        outcomes.Should().ContainSingle().Which.Status.Should().Be(WriteStatus.Rejected);
        service.Desired.FanProfile.Should().Be("Silent");
    }

    [Fact]
    public async Task A_switch_that_cannot_be_saved_still_happens_in_memory_and_is_logged()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), """{ "schemaVersion": 2 }""");
        var store = new SettingsStore(_folder);
        store.Load();
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, store, On(battery: SilentEco), _log);
        await service.SwitchPowerSourceAsync(PowerSource.Ac, write: false);

        var outcomes = await service.SwitchPowerSourceAsync(PowerSource.Battery, write: true);

        outcomes.Should().ContainSingle().Which.Status.Should().Be(WriteStatus.Applied);
        service.Desired.FanProfile.Should().Be("Silent");
        _log.Lines.Should().Contain(l => l.Contains("settings.json"));
    }
}
