using Microsoft.Extensions.Time.Testing;
using MTool.App.Hardware;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;
using MTool.Core.Sensors;
using MTool.Core.Settings;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.ViewModels;

/// <summary>
/// Experimental control in the WMI2 window (plan asama15 15c-5; CLAUDE.md "WMI2 deneysel yazma"): offered only
/// on an exact list entry, turned on only after a risk question (No by default), kept per firmware so it opens
/// by itself until a BIOS update, and every write goes through the gateway with the write-path test first.
/// </summary>
public sealed partial class Wmi2ControlsViewModelTests : IDisposable
{
    private const string Exact = "15M3EMS1.105";
    private const string PreStatePath = @"C:\M-Tool\pre-mtool-state-wmi2.json";
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.Load(
        """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105"],
          "families": ["15M3EMS1.1"],
          "familyOverrides": {}
        }
        """,
        [P65()]);

    private readonly FakeWmi2LiveSource _readOnly = new() { Firmware = Exact };
    private readonly FakeWmi2Control _control = new();
    private readonly FakeConfirm _confirm = new();
    private readonly FakeWmi2Consent _consent = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero));
    private readonly ListLog _log = new();
    private readonly List<string> _openedFor = [];
    private readonly Wmi2WriteJournal _journal = new();
    private Func<Wmi2ControlOpening> _opening;
    private int _readOnlyOpens;
    private bool _readOnlyFailsToOpen;
    private Wmi2LiveViewModel? _live;
    private Wmi2ControlsViewModel? _controls;

    public Wmi2ControlsViewModelTests() => _opening = () => Wmi2ControlOpening.Opened(_control);

    public void Dispose()
    {
        _controls?.Dispose();
        _live?.Dispose();
    }

    private Wmi2LiveViewModel Live => _live!;

    private Wmi2ControlsViewModel Create(Wmi2FirmwareList? list = null)
    {
        _live = new Wmi2LiveViewModel(
            () =>
            {
                _readOnlyOpens++;
                return _readOnlyFailsToOpen ? null : _readOnly;
            }, new Wmi2CircuitBreaker(), _confirm, () => true, _time, _log, WmiInterface.Wmi2, list ?? List);
        _controls = new Wmi2ControlsViewModel(Live, OpenControl, _confirm, _consent, _time, _log, WmiInterface.Wmi2, list ?? List, PreStatePath, _journal);
        return _controls;
    }

    private Task<Wmi2ControlOpening> OpenControl(string firmware, CancellationToken cancellationToken)
    {
        _openedFor.Add(firmware);
        return Task.FromResult(_opening());
    }

    /// <summary>Reading started (the reading question answered yes) and the first round shown.</summary>
    private async Task<Wmi2ControlsViewModel> ReadingAsync(Wmi2FirmwareList? list = null)
    {
        var controls = Create(list);
        await Live.ShowCommand.ExecuteAsync(null);
        return controls;
    }

    /// <summary>Lets the 2 s rule before the switch pass and waits until the state settles.</summary>
    private void PassMinIntervalUntil(Func<bool> done)
    {
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        SpinWait.SpinUntil(done, Settle).Should().BeTrue();
    }

    private async Task<Wmi2ControlsViewModel> OpenAsync()
    {
        var controls = await ReadingAsync();
        var enabling = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => enabling.IsCompleted);
        await enabling;
        controls.State.Should().Be(Wmi2ControlState.Open);
        return controls;
    }

    [Fact]
    public void Before_reading_starts_there_is_nothing_to_offer()
    {
        var controls = Create();

        controls.State.Should().Be(Wmi2ControlState.Hidden);
        controls.EnableCommand.CanExecute(null).Should().BeFalse();
        controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
    }

    [Fact]
    public async Task An_exact_firmware_without_consent_is_offered_and_nothing_is_opened()
    {
        var controls = await ReadingAsync();

        controls.State.Should().Be(Wmi2ControlState.Offered);
        controls.Reason.Should().BeNull();
        controls.EnableCommand.CanExecute(null).Should().BeTrue();
        controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
        _openedFor.Should().BeEmpty();
        _confirm.Questions.Should().Equal(Strings.Wmi2_Confirm);
    }

    [Theory]
    [InlineData("15M3EMS1.199")] // family only
    [InlineData("14F1EMS1.112")] // not in the list
    public async Task A_firmware_that_is_not_an_exact_entry_is_not_offered_and_says_why(string firmware)
    {
        _readOnly.Firmware = firmware;

        var controls = await ReadingAsync();

        controls.State.Should().Be(Wmi2ControlState.NotOffered);
        controls.Reason.Should().Be(Strings.Wmi2_ControlNotExact);
        controls.EnableCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Without_the_list_nothing_is_offered_and_it_says_why()
    {
        var controls = Create(list: null);
        _controls!.Dispose();
        _live!.Dispose();
        _live = new Wmi2LiveViewModel(() => _readOnly, new Wmi2CircuitBreaker(), _confirm, () => true, _time, _log, WmiInterface.Wmi2, list: null);
        _controls = new Wmi2ControlsViewModel(Live, OpenControl, _confirm, _consent, _time, _log, WmiInterface.Wmi2, list: null, PreStatePath, _journal);

        await Live.ShowCommand.ExecuteAsync(null);

        _controls.State.Should().Be(Wmi2ControlState.NotOffered);
        _controls.Reason.Should().Be(Strings.Wmi2_ControlListUnavailable);
        controls.Should().NotBeSameAs(_controls);
    }

    [Fact]
    public async Task Turning_on_asks_with_no_as_the_default_and_no_changes_nothing()
    {
        var controls = await ReadingAsync();
        _confirm.Answer = false;

        await controls.EnableCommand.ExecuteAsync(null);

        _confirm.Questions.Should().Equal(Strings.Wmi2_Confirm, Strings.Wmi2_ControlConfirm);
        _confirm.DefaultsToYes[^1].Should().BeFalse();
        _consent.Saves.Should().BeEmpty();
        _openedFor.Should().BeEmpty();
        _readOnly.Disposed.Should().BeFalse();
        controls.State.Should().Be(Wmi2ControlState.Offered);
    }

    [Fact]
    public void The_question_names_the_risks_and_the_way_back()
    {
        Strings.Wmi2_ControlConfirm.Should().Contain(Strings.Wmi2_ControlRevert).And.Contain("MSI Center").And.Contain("BIOS");
    }

    [Fact]
    public async Task Yes_saves_the_consent_for_this_firmware_closes_reading_and_opens_control()
    {
        var controls = await OpenAsync();

        _consent.Saves.Should().Equal(new Wmi2ExperimentalConsent(Exact, _time.GetUtcNow() - Wmi2ReadPolicy.MinInterval));
        _readOnly.Disposed.Should().BeTrue();
        _openedFor.Should().Equal(Exact);
        Live.Source.Should().BeSameAs(_control);
        controls.Reason.Should().BeNull();
        _control.Calls.Should().BeEmpty("opening control writes nothing");
    }

    [Fact]
    public async Task A_saved_consent_for_this_firmware_opens_control_by_itself_without_asking()
    {
        _consent.Current = new Wmi2ExperimentalConsent(Exact, DateTimeOffset.UnixEpoch);

        var controls = await ReadingAsync();
        PassMinIntervalUntil(() => controls.State == Wmi2ControlState.Open);

        _confirm.Questions.Should().Equal(Strings.Wmi2_Confirm);
        _openedFor.Should().Equal(Exact);
        _consent.Saves.Should().BeEmpty();
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_consent_for_another_firmware_is_not_used_and_the_bios_update_is_named()
    {
        _consent.Current = new Wmi2ExperimentalConsent("15M3EMS1.104", DateTimeOffset.UnixEpoch);

        var controls = await ReadingAsync();
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        Thread.Sleep(100);

        controls.State.Should().Be(Wmi2ControlState.Offered);
        controls.Reason.Should().Be(Strings.Wmi2_ControlFirmwareChanged);
        _openedFor.Should().BeEmpty();
    }

    [Fact]
    public async Task After_a_bios_update_yes_replaces_the_old_consent()
    {
        _consent.Current = new Wmi2ExperimentalConsent("15M3EMS1.104", DateTimeOffset.UnixEpoch);
        var controls = await ReadingAsync();

        var enabling = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => enabling.IsCompleted);

        _confirm.Questions.Should().HaveCount(2);
        _consent.Current!.Firmware.Should().Be(Exact);
        controls.State.Should().Be(Wmi2ControlState.Open);
    }

    [Theory]
    [InlineData("NotWmi2", nameof(Strings.Wmi2_ControlNotWmi2))]
    [InlineData("FirmwareUnreadable", nameof(Strings.Wmi2_ControlFirmwareUnreadable))]
    [InlineData("OtherFirmware", nameof(Strings.Wmi2_ControlFirmwareChanged))]
    public async Task Control_that_does_not_open_says_why_and_reading_goes_on(string reason, string key)
    {
        _opening = () => Wmi2ControlOpening.NotOpened(Enum.Parse<Wmi2ControlUnavailable>(reason));
        var controls = await ReadingAsync();

        var enabling = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => enabling.IsCompleted);

        controls.State.Should().Be(Wmi2ControlState.Unavailable);
        controls.Reason.Should().Be(Strings.ResourceManager.GetString(key, Strings.Culture));
        Live.Source.Should().BeSameAs(_readOnly);
        _readOnlyOpens.Should().Be(2);
        controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
    }

    [Fact]
    public async Task Control_that_throws_while_opening_says_it_could_not_open()
    {
        _opening = () => throw new EcAccessException("MSI_ACPI could not be opened");
        var controls = await ReadingAsync();

        var enabling = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => enabling.IsCompleted);

        controls.State.Should().Be(Wmi2ControlState.Unavailable);
        controls.Reason.Should().Be(Strings.Wmi2_ControlOpenFailed);
        controls.Reason.Should().NotContain("MSI_ACPI");
    }

    [Fact]
    public async Task Unavailable_control_can_be_tried_again_without_asking_again()
    {
        _opening = () => Wmi2ControlOpening.NotOpened(Wmi2ControlUnavailable.FirmwareUnreadable);
        var controls = await ReadingAsync();
        var enabling = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => enabling.IsCompleted);
        _opening = () => Wmi2ControlOpening.Opened(_control);

        controls.EnableCommand.CanExecute(null).Should().BeTrue();
        var again = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => again.IsCompleted);

        controls.State.Should().Be(Wmi2ControlState.Open);
        _confirm.Questions.Should().HaveCount(2, "the consent was given once");
    }

    [Fact]
    public async Task A_consent_that_cannot_be_saved_still_opens_control_for_this_session_and_says_so()
    {
        _consent.Error = new IOException("settings.json is read-only");
        var controls = await OpenAsync();

        controls.Result.Should().Be(Strings.Wmi2_ConsentNotSaved);
        controls.ResultKind.Should().Be(MessageKind.Warning);
        Live.Source.Should().BeSameAs(_control);
    }

    [Fact]
    public async Task The_first_write_runs_the_write_path_test_first_and_the_next_does_not()
    {
        var controls = await OpenAsync();

        await controls.SetCoolerBoostCommand.ExecuteAsync(true);
        await controls.SetCoolerBoostCommand.ExecuteAsync(false);

        _control.Calls.Should().Equal(
            "test", $"apply {new Wmi2Change.CoolerBoost(true)}", $"apply {new Wmi2Change.CoolerBoost(false)}");
        controls.Result.Should().Be(Strings.Wmi2_OutcomeApplied);
        controls.ResultKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public async Task A_failed_write_path_test_stops_the_write_and_says_why()
    {
        _control.TestOutcome = new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, Block: Wmi2WriteBlock.NoPreState, Problem: Wmi2GatewayProblem.PreStateOtherFirmware);
        var controls = await OpenAsync();

        await controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.High);

        _control.Calls.Should().Equal("test");
        controls.Result.Should().Be(string.Format(Strings.Wmi2_ProblemPreStateOtherFirmware, PreStatePath));
        controls.ResultKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public async Task In_dry_run_the_write_path_test_is_not_needed_and_the_write_goes_on()
    {
        _control.TestOutcome = new Wmi2WriteOutcome(WriteStatus.DryRun, 0xD2, 0xC1, new RegisterWrite(0xD2, 0xC1));
        _control.ApplyOutcome = change => new Wmi2WriteOutcome(WriteStatus.DryRun, change.Register, 0x00, new RegisterWrite(change.Register, 0x01));
        var controls = await OpenAsync();

        await controls.SetFanModeCommand.ExecuteAsync(Wmi2FanMode.Silent);

        _control.Calls.Should().Equal("test", $"apply {new Wmi2Change.FanMode(Wmi2FanMode.Silent)}");
        controls.Result.Should().Be(Strings.Wmi2_OutcomeDryRun);
    }

    [Fact]
    public async Task Each_switch_asks_for_its_own_change()
    {
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();

        await controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.Eco);
        await controls.SetFanModeCommand.ExecuteAsync(Wmi2FanMode.Auto);
        await controls.SetChargeLimitCommand.ExecuteAsync(80);

        _control.Calls.Should().Equal(
            $"apply {new Wmi2Change.Performance(PerformanceMode.Eco)}",
            $"apply {new Wmi2Change.FanMode(Wmi2FanMode.Auto)}",
            $"apply {new Wmi2Change.ChargeLimit(80)}");
    }

    [Fact]
    public async Task No_question_follows_a_write()
    {
        var controls = await OpenAsync();
        var asked = _confirm.Questions.Count;

        await controls.SetCoolerBoostCommand.ExecuteAsync(true);

        _confirm.Questions.Should().HaveCount(asked);
    }

    [Fact]
    public async Task A_write_that_throws_says_it_failed_without_error_text()
    {
        var controls = await OpenAsync();
        _control.Error = new EcAccessException("Set_Data(0x98) did not answer within 2 s");

        await controls.SetCoolerBoostCommand.ExecuteAsync(true);

        controls.Result.Should().Be(Strings.Wmi2_ProblemWriteFailed);
        controls.ResultKind.Should().Be(MessageKind.Warning);
        _log.Lines.Should().Contain(line => line.Contains("Set_Data(0x98)", StringComparison.Ordinal));
        controls.IsWriting.Should().BeFalse();
    }

    [Theory]
    [InlineData(0xC5, true)]
    [InlineData(0xC1, false)]
    public async Task Changing_mode_over_msi_centers_fourth_mode_asks_first(byte performanceRaw, bool asks)
    {
        _control.Reads.Status = _control.Reads.Status with { PerformanceRaw = performanceRaw };
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        await RoundThroughControlAsync();
        _confirm.Answer = false;
        var asked = _confirm.Questions.Count;

        await controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.High);

        _confirm.Questions.Should().HaveCount(asked + (asks ? 1 : 0));
        if (asks)
        {
            _confirm.Questions[^1].Should().Be(Strings.Wmi2_WarnC5);
        }

        _control.Calls.Should().HaveCount(asks ? 0 : 1);
    }

    [Theory]
    [InlineData(0x8D, true)]
    [InlineData(0x9D, true)]
    [InlineData(0x0D, false)]
    public async Task Auto_or_silent_over_a_custom_fan_curve_asks_first(byte fanModeRaw, bool asks)
    {
        _control.Reads.Status = _control.Reads.Status with { FanModeRaw = fanModeRaw };
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        await RoundThroughControlAsync();
        _confirm.Answer = false;
        var asked = _confirm.Questions.Count;

        await controls.SetFanModeCommand.ExecuteAsync(Wmi2FanMode.Silent);

        _confirm.Questions.Should().HaveCount(asked + (asks ? 1 : 0));
        if (asks)
        {
            _confirm.Questions[^1].Should().Be(Strings.Wmi2_WarnCustomCurve);
        }

        _control.Calls.Should().HaveCount(asks ? 0 : 1);
    }

    [Theory]
    [InlineData(0x80 | 30, true)]
    [InlineData(0x80 | 49, true)]
    [InlineData(0x80 | 50, false)]
    public async Task A_new_charge_limit_over_one_under_50_asks_first(byte chargeRaw, bool asks)
    {
        _control.Reads.Status = _control.Reads.Status with { ChargeRaw = chargeRaw };
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        await RoundThroughControlAsync();
        _confirm.Answer = false;
        var asked = _confirm.Questions.Count;

        await controls.SetChargeLimitCommand.ExecuteAsync(80);

        _confirm.Questions.Should().HaveCount(asked + (asks ? 1 : 0));
        if (asks)
        {
            _confirm.Questions[^1].Should().Be(string.Format(Strings.Wmi2_WarnLowChargeLimit, chargeRaw & 0x7F));
        }

        _control.Calls.Should().HaveCount(asks ? 0 : 1);
    }

    [Fact]
    public async Task Yes_to_a_warning_writes_and_the_warning_has_no_as_the_default()
    {
        _control.Reads.Status = _control.Reads.Status with { PerformanceRaw = 0xC5 };
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        await RoundThroughControlAsync();

        await controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.High);

        _confirm.DefaultsToYes[^1].Should().BeFalse();
        _control.Calls.Should().Equal($"apply {new Wmi2Change.Performance(PerformanceMode.High)}");
    }

    [Fact]
    public async Task Back_to_before_m_tool_runs_the_test_first_and_shows_every_register()
    {
        _control.RevertOutcomes =
        [
            new(WriteStatus.Applied, 0x98, 0x82, new RegisterWrite(0x98, 0x02)),
            new(WriteStatus.Applied, 0xD2, 0xC1),
            new(WriteStatus.Rejected, 0xD4, Block: Wmi2WriteBlock.SessionLocked),
            new(WriteStatus.Rejected, 0xD7, Problem: Wmi2GatewayProblem.Cancelled),
        ];
        var controls = await OpenAsync();

        await controls.RevertCommand.ExecuteAsync(null);

        _control.Calls.Should().Equal("test", "revert");
        var expected = Wmi2OutcomeText.Revert(_control.RevertOutcomes, PreStatePath);
        controls.RevertLines.Should().Equal(expected.Lines);
        (controls.Result, controls.ResultKind).Should().Be((expected.Summary, MessageKind.Warning));
    }

    [Fact]
    public async Task Back_to_before_m_tool_that_all_held_is_information()
    {
        var controls = await OpenAsync();

        await controls.RevertCommand.ExecuteAsync(null);

        controls.RevertLines.Should().HaveCount(Wmi2WriteCeiling.Registers.Count);
        (controls.Result, controls.ResultKind).Should().Be((Strings.Wmi2_RevertDone, MessageKind.Info));
    }

    [Fact]
    public async Task A_layout_that_does_not_fit_closes_every_switch_and_shows_the_raw_bytes()
    {
        _control.Layout = Wmi2LayoutCheck.Check(new Wmi2Status(0x02, 0x00, 0x0D, 0xE4));
        var controls = await OpenAsync();

        controls.Reason.Should().Be(string.Format(Strings.Wmi2_ControlLayout, "0xD2=0x00"));
        controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
        controls.RevertCommand.CanExecute(null).Should().BeFalse();
        controls.TurnOffCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Turning_off_forgets_the_consent_and_goes_back_to_reading_only()
    {
        var controls = await OpenAsync();
        var asked = _confirm.Questions.Count;

        var turningOff = controls.TurnOffCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => turningOff.IsCompleted);

        _consent.Current.Should().BeNull();
        _control.Disposed.Should().BeTrue();
        Live.Source.Should().BeSameAs(_readOnly);
        controls.State.Should().Be(Wmi2ControlState.Offered);
        controls.Reason.Should().BeNull();
        _confirm.Questions.Should().HaveCount(asked);
        _control.Calls.Should().BeEmpty("turning off writes nothing");
    }

    [Fact]
    public async Task Turning_off_when_the_file_cannot_be_saved_still_closes_control_and_says_so()
    {
        var controls = await OpenAsync();
        _consent.Error = new IOException("locked");

        var turningOff = controls.TurnOffCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => turningOff.IsCompleted);
        _time.Advance(Wmi2LiveViewModel.PollInterval);
        Thread.Sleep(100);

        Live.Source.Should().BeSameAs(_readOnly);
        controls.Result.Should().Be(Strings.Wmi2_ConsentNotSaved);
        _openedFor.Should().HaveCount(1, "control does not open by itself again in this session");
    }

    [Fact]
    public async Task Stopping_the_reading_hides_control()
    {
        var controls = await OpenAsync();

        Live.StopCommand.Execute(null);

        controls.State.Should().Be(Wmi2ControlState.Hidden);
        _control.Disposed.Should().BeTrue();
        controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
    }

    [Fact]
    public async Task The_switches_show_what_the_last_round_read()
    {
        _readOnly.Status = new Wmi2Status(CoolerBoostRaw: 0x82, PerformanceRaw: 0xC2, FanModeRaw: 0x1D, ChargeRaw: 0x80 | 60);

        var controls = await ReadingAsync();

        controls.CoolerBoostOn.Should().BeTrue();
        controls.Performance.Should().Be(PerformanceMode.Eco);
        controls.FanMode.Should().Be(Wmi2FanMode.Silent);
        controls.ChargeLimitPercent.Should().Be(60);
    }

    [Fact]
    public async Task After_a_write_the_next_round_reads_the_status()
    {
        var controls = await OpenAsync();
        Live.SetWindowShown(true);
        _time.Advance(Wmi2LiveViewModel.PollInterval); // the round after the switch reads the status
        SpinWait.SpinUntil(() => _control.Reads.StatusReads == 1 && !Live.IsBusy, Settle).Should().BeTrue();
        _time.Advance(Wmi2LiveViewModel.PollInterval);
        SpinWait.SpinUntil(() => _control.Reads.SensorReads == 2 && !Live.IsBusy, Settle).Should().BeTrue();
        _control.Reads.StatusReads.Should().Be(1, "a timed round reads the sensors only");

        await controls.SetCoolerBoostCommand.ExecuteAsync(true);
        _time.Advance(Wmi2LiveViewModel.PollInterval);

        SpinWait.SpinUntil(() => _control.Reads.StatusReads == 2, Settle).Should().BeTrue();
    }

    [Fact]
    public async Task Closing_the_window_while_auto_open_waits_opens_nothing()
    {
        _consent.Current = new Wmi2ExperimentalConsent(Exact, DateTimeOffset.UnixEpoch);
        var controls = await ReadingAsync();
        controls.State.Should().Be(Wmi2ControlState.Opening);

        Live.Dispose();
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        SpinWait.SpinUntil(() => controls.State != Wmi2ControlState.Opening, Settle).Should().BeTrue();

        _openedFor.Should().BeEmpty();
    }

    [Fact]
    public async Task Turning_on_waits_while_a_round_is_running()
    {
        var controls = await ReadingAsync();
        _readOnly.HoldStatus = new TaskCompletionSource();
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        var refreshing = Live.RefreshCommand.ExecuteAsync(null);

        controls.EnableCommand.CanExecute(null).Should().BeFalse();
        _readOnly.HoldStatus.SetResult();
        await refreshing;
        controls.EnableCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task While_a_write_runs_reading_cannot_stop_or_switch_and_control_cannot_turn_off()
    {
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        _control.HoldApply = new TaskCompletionSource();

        var writing = controls.SetCoolerBoostCommand.ExecuteAsync(true);

        Live.StopCommand.CanExecute(null).Should().BeFalse();
        controls.TurnOffCommand.CanExecute(null).Should().BeFalse();
        (await Live.SwitchSourceAsync(_ => Task.FromResult<IWmi2LiveSource?>(null)).WaitAsync(Settle)).Should().BeFalse();
        _control.HoldApply.SetResult();
        await writing;
        Live.StopCommand.CanExecute(null).Should().BeTrue();
        _control.Disposed.Should().BeFalse();
    }

    [Fact]
    public async Task While_turning_off_nothing_can_be_written()
    {
        var controls = await OpenAsync();

        var turningOff = controls.TurnOffCommand.ExecuteAsync(null);

        controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
        controls.RevertCommand.CanExecute(null).Should().BeFalse();
        PassMinIntervalUntil(() => turningOff.IsCompleted);
    }

    [Fact]
    public async Task A_turn_off_that_could_not_switch_keeps_the_consent()
    {
        var controls = await OpenAsync();

        var turningOff = controls.TurnOffCommand.ExecuteAsync(null);
        Live.Dispose(); // the window closed during the 2 s wait
        PassMinIntervalUntil(() => turningOff.IsCompleted);

        _consent.Saves.Should().HaveCount(1, "only the consent given when turning on was saved");
        _consent.Current.Should().NotBeNull();
    }

    [Fact]
    public async Task Writes_are_closed_while_reading_has_stopped()
    {
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        _control.Reads.Error = FakeWmi2LiveSource.Timeout();

        for (var i = 0; i < Wmi2CircuitBreaker.MaxFailuresInARow; i++)
        {
            _time.Advance(Wmi2ReadPolicy.MinInterval);
            await Live.RefreshCommand.ExecuteAsync(null);
        }

        Live.IsStopped.Should().BeTrue();
        controls.SetCoolerBoostCommand.CanExecute(true).Should().BeFalse();
        controls.RevertCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task When_neither_control_nor_reading_opens_control_is_hidden()
    {
        _opening = () => Wmi2ControlOpening.NotOpened(Wmi2ControlUnavailable.FirmwareUnreadable);
        var controls = await ReadingAsync();
        _readOnlyFailsToOpen = true;

        var enabling = controls.EnableCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => enabling.IsCompleted);

        Live.IsUnavailable.Should().BeTrue();
        controls.State.Should().Be(Wmi2ControlState.Hidden);
        controls.EnableCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Control_turned_off_stays_off_after_stop_and_show_even_when_it_was_not_saved()
    {
        var controls = await OpenAsync();
        _consent.Error = new IOException("locked");
        var turningOff = controls.TurnOffCommand.ExecuteAsync(null);
        PassMinIntervalUntil(() => turningOff.IsCompleted);

        Live.StopCommand.Execute(null);
        var showing = Live.ShowCommand.ExecuteAsync(null); // within 2 s of the last round: it waits for the rule
        PassMinIntervalUntil(() => showing.IsCompleted);
        _time.Advance(Wmi2ReadPolicy.MinInterval);

        controls.State.Should().Be(Wmi2ControlState.Offered);
        _openedFor.Should().HaveCount(1);
    }

    [Fact]
    public async Task Reading_cannot_stop_while_a_warning_is_open_and_a_window_closed_meanwhile_writes_nothing()
    {
        _control.Reads.Status = _control.Reads.Status with { PerformanceRaw = 0xC5 };
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        await RoundThroughControlAsync();
        bool? couldStop = null;
        _confirm.WhileAsking = () =>
        {
            couldStop = Live.StopCommand.CanExecute(null);
            controls.Dispose();
            Live.Dispose();
        };

        await controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.High);

        couldStop.Should().BeFalse();
        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Control_gone_while_a_warning_is_open_writes_nothing()
    {
        _control.Reads.Status = _control.Reads.Status with { PerformanceRaw = 0xC5 };
        _control.IsWritePathTested = true;
        var controls = await OpenAsync();
        await RoundThroughControlAsync();
        _confirm.WhileAsking = () => Live.Dispose(); // the source is closed; this view model is not

        await controls.SetPerformanceCommand.ExecuteAsync(PerformanceMode.High);

        _control.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_source_that_throws_while_closing_does_not_stop_control_from_opening()
    {
        _consent.Current = new Wmi2ExperimentalConsent(Exact, DateTimeOffset.UnixEpoch);
        _readOnly.DisposeError = new InvalidOperationException("close failed");

        var controls = await ReadingAsync();
        PassMinIntervalUntil(() => controls.State == Wmi2ControlState.Open);

        _log.Lines.Should().Contain(line => line.StartsWith("WARN", StringComparison.Ordinal) && line.Contains("close failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stop_clears_what_the_last_write_said()
    {
        var controls = await OpenAsync();
        await controls.RevertCommand.ExecuteAsync(null);

        Live.StopCommand.Execute(null);

        controls.Result.Should().BeNull();
        controls.RevertLines.Should().BeEmpty();
        controls.Reason.Should().BeNull();
    }

    /// <summary>One round through control, so its status is what the switches and warnings go by.</summary>
    private async Task RoundThroughControlAsync()
    {
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await Live.RefreshCommand.ExecuteAsync(null);
        Live.LastStatus.Should().Be(_control.Reads.Status);
    }
}
