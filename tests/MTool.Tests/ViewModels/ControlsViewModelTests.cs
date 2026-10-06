using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public sealed class ControlsViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly FakeNotifier _notifier = new();
    private readonly ProfileService _service;
    private readonly StatusViewModel _status;
    private readonly ControlsViewModel _controls;

    public ControlsViewModelTests()
    {
        // No performance or charge choice: the drift band then follows the fan table alone.
        var noChoices = AppSettings.Default with { Desired = new DesiredState(DesiredState.DefaultProfileName) };
        _service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), noChoices, new ListLog());
        _status = new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System);
        _controls = new ControlsViewModel(_service, _control, _status, new ImmediateDispatcher());
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Lists_the_profiles()
    {
        _controls.ProfileNames.Should().Equal("Default", "Cool", "Silent");
    }

    [Fact]
    public async Task The_profile_list_follows_added_and_renamed_profiles()
    {
        var changed = new List<string?>();
        _controls.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await _service.AddProfileAsync("Gece", TestCurves.Night);
        _controls.ProfileNames.Should().Equal("Default", "Cool", "Silent", "Gece");

        await _service.RenameProfileAsync("Gece", "Uyku");
        _controls.ProfileNames.Should().Equal("Default", "Cool", "Silent", "Uyku");
        changed.Should().Contain(nameof(ControlsViewModel.ProfileNames));
    }

    [Fact]
    public async Task A_catalog_change_is_shown_on_the_ui_thread()
    {
        var ui = new QueuedDispatcher();
        var controls = new ControlsViewModel(_service, _control, _status, ui);

        await _service.AddProfileAsync("Gece", TestCurves.Night);
        controls.ProfileNames.Should().NotContain("Gece", "the event arrives on a pool thread");

        ui.RunAll();
        controls.ProfileNames.Should().Contain("Gece");
    }

    [Fact]
    public async Task A_catalog_change_before_the_first_ec_read_only_updates_the_list()
    {
        await _service.AddProfileAsync("Gece", TestCurves.Night);

        _controls.ProfileNames.Should().Contain("Gece");
        _controls.ActiveProfile.Should().BeNull();
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_catalog_change_while_a_command_runs_leaves_the_drift_band_to_the_command()
    {
        var ui = new QueuedDispatcher();
        var controls = new ControlsViewModel(_service, _control, _status, ui);
        await _service.AddProfileAsync("Gece", TestCurves.Night);
        await _service.SelectProfileAsync("Gece");
        await controls.RefreshAsync(PortUse.None);
        ui.RunAll();
        _status.ShowReapply.Should().BeFalse();

        var refreshAfterCommand = new TaskCompletionSource();
        _control.NextReadGate = refreshAfterCommand;
        var command = controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.Eco);
        await _service.SaveCurvesAsync("Gece", TestCurves.QuieterNight);
        ui.RunAll();

        controls.IsBusy.Should().BeTrue();
        _status.ShowReapply.Should().BeFalse("the EC is between states while the command runs");

        refreshAfterCommand.SetResult();
        await command;
        _status.ShowReapply.Should().BeTrue("the EC still holds the curve saved before");
    }

    [Fact]
    public async Task A_custom_profile_can_be_selected_from_the_list()
    {
        await _service.AddProfileAsync("Gece", TestCurves.Night);

        await _controls.SelectProfileCommand.ExecuteAsync("Gece");

        _control.Calls.Should().Contain("fan Gece");
        _controls.ActiveProfile.Should().Be("Gece");
    }

    [Fact]
    public async Task The_label_names_the_desired_profile_when_a_copy_has_the_same_tables()
    {
        await _service.AddProfileAsync("Cool kopya", Presets.Cool.Curves);

        await _controls.SelectProfileCommand.ExecuteAsync("Cool kopya");
        _controls.ActiveProfile.Should().Be("Cool kopya");

        await _controls.SelectProfileCommand.ExecuteAsync("Cool");
        _controls.ActiveProfile.Should().Be("Cool");
    }

    [Fact]
    public async Task A_deleted_profile_whose_table_stays_in_the_ec_is_labelled_unknown()
    {
        await _service.AddProfileAsync("Gece", TestCurves.Night);
        _control.State = _control.State with { FanCurves = TestCurves.Night };
        await _controls.RefreshAsync(PortUse.None);
        _controls.ActiveProfile.Should().Be("Gece");

        await _service.DeleteProfileAsync("Gece");

        _controls.ActiveProfile.Should().BeNull();
        _controls.ActiveProfileLabel.Should().Be("Tanınmayan fan ayarı");
    }

    [Fact]
    public async Task Saving_the_desired_profile_shows_the_drift_from_the_ec_at_once()
    {
        await _service.AddProfileAsync("Gece", TestCurves.Night);
        await _controls.SelectProfileCommand.ExecuteAsync("Gece");
        _status.ShowReapply.Should().BeFalse();
        var calls = _control.Calls.Count;

        await _service.SaveCurvesAsync("Gece", TestCurves.QuieterNight);

        _control.Calls.Should().HaveCount(calls, "saving neither writes nor reads the EC");
        _status.ShowReapply.Should().BeTrue("the EC still holds the old curve");
        _controls.ActiveProfile.Should().BeNull("the EC table no longer matches a saved profile");
    }

    [Fact]
    public async Task Refresh_shows_what_the_ec_holds()
    {
        _control.State = _control.State with { FanCurves = Presets.Silent.Curves, Port = new PortState(0x82, 0xBC) };

        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.ActiveProfile.Should().Be("Silent");
        _controls.ActivePerformance.Should().Be(PerformanceMode.High);
        _controls.CoolerBoostOn.Should().BeTrue();
        _controls.ChargeLimitActual.Should().Be(60);
        _controls.ChargeLimitLabel.Should().Be("%60");
        _controls.ChargeLimitDraft.Should().Be(60);
    }

    [Fact]
    public async Task An_unread_port_state_is_shown_as_unknown()
    {
        _control.State = _control.State with { Port = null };

        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.CoolerBoostOn.Should().BeNull();
        _controls.ChargeLimitActual.Should().BeNull();
        _controls.ChargeLimitLabel.Should().Be("bilinmiyor");
        _controls.ChargeLimitDraft.Should().Be(_controls.ChargeLimitMax);
    }

    [Fact]
    public async Task A_disabled_charge_limit_is_shown_as_off()
    {
        _control.State = _control.State with { Port = new PortState(0x02, 0x64) };

        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.ChargeLimitLabel.Should().Be("kapalı");
    }

    [Fact]
    public async Task Without_a_port_cooler_boost_and_charge_limit_are_disabled()
    {
        _control.Access = _control.Access with { PortFeaturesAvailable = false };

        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
        _controls.ApplyChargeLimitCommand.CanExecute(null).Should().BeFalse();
        _controls.SelectProfileCommand.CanExecute("Cool").Should().BeTrue();
        _controls.CanWritePort.Should().BeFalse();
    }

    [Fact]
    public async Task Factory_state_after_a_reboot_is_labelled()
    {
        _control.State = _control.State with { Performance = null, PerformanceRaw = 0x80, FanCurves = Tweaked() };

        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.ActiveProfile.Should().BeNull();
        _controls.ActiveProfileLabel.Should().Contain("Tanınmayan");
        _controls.PerformanceLabel.Should().Contain("fabrika ayarı");
    }

    [Fact]
    public async Task Selecting_a_profile_writes_it_and_shows_the_ec_state_after()
    {
        await _controls.RefreshAsync(PortUse.Allowed);

        await _controls.SelectProfileCommand.ExecuteAsync("Cool");

        _control.Calls.Should().Contain("fan Cool");
        _controls.ActiveProfile.Should().Be("Cool");
        _status.MessageKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public async Task A_rejected_profile_leaves_the_real_selection()
    {
        await _controls.RefreshAsync(PortUse.Allowed);
        _control.NextStatus = WriteStatus.Rejected;

        await _controls.SelectProfileCommand.ExecuteAsync("Cool");

        _controls.ActiveProfile.Should().Be("Default");
        _status.MessageKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public async Task Locked_writes_disable_every_write_command()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.Locked, LockReason = "x" };

        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.CanWrite.Should().BeFalse();
        _controls.SelectProfileCommand.CanExecute("Cool").Should().BeFalse();
        _controls.SetPerformanceCommand.CanExecute(PerformanceMode.Eco).Should().BeFalse();
        _controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
        _controls.ApplyChargeLimitCommand.CanExecute(null).Should().BeFalse();
        _controls.ReapplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Dry_run_still_allows_commands()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.DryRun };

        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.SelectProfileCommand.CanExecute("Cool").Should().BeTrue();
    }

    [Fact]
    public async Task Moving_the_charge_slider_does_not_write_until_applied()
    {
        await _controls.RefreshAsync(PortUse.Allowed);

        _controls.ChargeLimitDraft = 60;
        _control.Calls.Should().NotContain(c => c.StartsWith("charge"));

        await _controls.ApplyChargeLimitCommand.ExecuteAsync(null);
        _control.Calls.Should().Contain("charge 60");
        _controls.ChargeLimitActual.Should().Be(60);
    }

    [Fact]
    public async Task Commands_are_disabled_while_a_write_runs()
    {
        await _controls.RefreshAsync(PortUse.Allowed);
        _control.WriteGate = new TaskCompletionSource();

        var running = _controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.Eco);
        _controls.IsBusy.Should().BeTrue();
        _controls.SelectProfileCommand.CanExecute("Cool").Should().BeFalse();

        _control.WriteGate.SetResult();
        await running;
        _controls.IsBusy.Should().BeFalse();
        _controls.SelectProfileCommand.CanExecute("Cool").Should().BeTrue();
        _controls.ActivePerformance.Should().Be(PerformanceMode.Eco);
    }

    [Fact]
    public async Task Cooler_boost_toggles()
    {
        await _controls.RefreshAsync(PortUse.Allowed);

        await _controls.SetCoolerBoostCommand.ExecuteAsync(true);

        _control.Calls.Should().Contain("boost True");
        _controls.CoolerBoostOn.Should().BeTrue();
    }

    [Fact]
    public async Task Commands_that_only_use_wmi_refresh_without_the_port()
    {
        await _controls.RefreshAsync(PortUse.Allowed);
        _control.StateReadPortUses.Clear();

        await _controls.SelectProfileCommand.ExecuteAsync("Cool");
        await _controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.Balanced);

        _control.StateReadPortUses.Should().Equal(PortUse.None, PortUse.None);
    }

    [Fact]
    public async Task Commands_that_use_the_port_refresh_with_it()
    {
        await _controls.RefreshAsync(PortUse.Allowed);
        _control.StateReadPortUses.Clear();

        await _controls.SetCoolerBoostCommand.ExecuteAsync(true);
        await _controls.ApplyChargeLimitCommand.ExecuteAsync(null);
        await _controls.ReapplyCommand.ExecuteAsync(null);

        _control.StateReadPortUses.Should().Equal(PortUse.Allowed, PortUse.Allowed, PortUse.Allowed);
    }

    [Fact]
    public async Task Drift_from_the_desired_state_offers_reapply()
    {
        await _service.SelectProfileAsync("Cool");
        _control.State = _control.State with { FanCurves = FactoryDefaults.FanCurves }; // as after a reboot

        await _controls.RefreshAsync(PortUse.Allowed);
        _status.ShowReapply.Should().BeTrue();

        await _controls.ReapplyCommand.ExecuteAsync(null);
        _control.Calls.Should().Contain(c => c.StartsWith("desired"));
        _status.ShowReapply.Should().BeFalse();
    }

    [Fact]
    public async Task A_state_read_failure_is_reported_not_thrown()
    {
        var failing = new ThrowingStateControl(_control);
        var controls = new ControlsViewModel(_service, failing, _status, new ImmediateDispatcher());

        await controls.RefreshAsync(PortUse.Allowed);

        _status.Message.Should().Contain("okunamadı");
        _status.MessageKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public void Value_type_commands_answer_false_for_a_missing_parameter()
    {
        // WPF queries CanExecute before CommandParameter is bound.
        _controls.SetPerformanceCommand.CanExecute(null).Should().BeFalse();
        _controls.SetCoolerBoostCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_state_read_after_a_failed_write_keeps_the_failure_message()
    {
        _control.NextStatus = WriteStatus.FailedRecovered;
        var failing = new ThrowingStateControl(_control);
        var controls = new ControlsViewModel(_service, failing, _status, new ImmediateDispatcher());

        await controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.Eco);

        _status.MessageKind.Should().Be(MessageKind.Error);
        _status.Message.Should().Contain("--unlock");
    }

    [Fact]
    public async Task A_command_started_while_another_runs_is_ignored()
    {
        await _controls.RefreshAsync(PortUse.Allowed);
        _control.WriteGate = new TaskCompletionSource();

        var first = _controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.Eco);
        var second = _controls.SelectProfileCommand.ExecuteAsync("Cool");
        _control.WriteGate.SetResult();
        await Task.WhenAll(first, second);

        _control.Calls.Should().NotContain("fan Cool");
        _controls.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task A_refresh_during_a_write_does_not_flash_the_drift_band()
    {
        await _service.SelectProfileAsync("Cool");
        await _controls.RefreshAsync(PortUse.Allowed);
        _status.ShowReapply.Should().BeFalse();
        _control.WriteGate = new TaskCompletionSource();

        var running = _controls.SelectProfileCommand.ExecuteAsync("Silent");
        await _controls.RefreshAsync(PortUse.Allowed); // e.g. the window was opened meanwhile
        _status.ShowReapply.Should().BeFalse();

        _control.WriteGate.SetResult();
        await running;
        _status.ShowReapply.Should().BeFalse();
    }

    [Fact]
    public void Charge_limit_bounds_come_from_the_write_rules()
    {
        _controls.ChargeLimitMin.Should().Be(EcWriteRules.MinChargeLimitPercent);
        _controls.ChargeLimitMax.Should().Be(EcWriteRules.MaxChargeLimitPercent);
    }

    private static FanCurves Tweaked()
    {
        var cpu = FactoryDefaults.FanCurves.Cpu.Points.ToArray();
        cpu[0] = cpu[0] with { SpeedPercent = 44 };
        return FactoryDefaults.FanCurves with { Cpu = new FanCurve(cpu) };
    }

    private sealed class ThrowingStateControl(FakeP65Control inner) : IP65Control
    {
        public DeviceAccess Access => inner.Access;

        public Task<ControlState> ReadControlStateAsync(PortUse portUse, CancellationToken cancellationToken = default) =>
            Task.FromException<ControlState>(new EcAccessException("EC hung"));

        public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken = default) => inner.ReadSensorsAsync(cancellationToken);

        public Task<WriteOutcome> ApplyFanProfileAsync(FanProfile profile, CancellationToken cancellationToken = default) =>
            inner.ApplyFanProfileAsync(profile, cancellationToken);

        public Task<WriteOutcome> SetCoolerBoostAsync(bool on, CancellationToken cancellationToken = default) =>
            inner.SetCoolerBoostAsync(on, cancellationToken);

        public Task<WriteOutcome> SetPerformanceAsync(PerformanceMode mode, CancellationToken cancellationToken = default) =>
            inner.SetPerformanceAsync(mode, cancellationToken);

        public Task<WriteOutcome> SetChargeLimitAsync(int percent, CancellationToken cancellationToken = default) =>
            inner.SetChargeLimitAsync(percent, cancellationToken);

        public Task<WriteOutcome> SetFanModeAsync(FanMode mode, CancellationToken cancellationToken = default) =>
            inner.SetFanModeAsync(mode, cancellationToken);

        public Task<IReadOnlyList<WriteOutcome>> ApplyDesiredAsync(
            DesiredState desired, ProfileCatalog catalog, CancellationToken cancellationToken = default) =>
            inner.ApplyDesiredAsync(desired, catalog, cancellationToken);
    }

    [Fact]
    public async Task A_slow_older_refresh_does_not_overwrite_a_newer_one()
    {
        var slowRead = new TaskCompletionSource();
        _control.NextReadGate = slowRead;
        var older = _controls.RefreshAsync(PortUse.Allowed);
        _control.State = _control.State with { FanCurves = Presets.Silent.Curves };

        await _controls.RefreshAsync(PortUse.None);
        slowRead.SetResult();
        await older;

        _controls.ActiveProfile.Should().Be("Silent");
    }
}
