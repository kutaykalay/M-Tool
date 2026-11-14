using MTool.Core.Device;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;

namespace MTool.Tests.ViewModels;

/// <summary>
/// The session record for the EC data submission (plan asama15 15c-6): every write that reached the EC, kept in
/// memory by <see cref="Wmi2WriteJournal"/>; nothing is asked after a write, the questions come in the preview.
/// </summary>
public sealed partial class Wmi2ControlsViewModelTests
{
    [Fact]
    public async Task Opening_control_names_the_firmware_and_layout_and_records_nothing_yet()
    {
        await OpenAsync();

        _journal.Snapshot().Should().BeNull();
        _journal.Record(new Wmi2WriteOutcome(WriteStatus.Applied, 0x98, 0x02, new RegisterWrite(0x98, 0x82)));
        var session = _journal.Snapshot()!;
        session.Firmware.Should().Be(Exact);
        session.LayoutStatus.Should().Be(_control.Layout.Status);
    }

    [Fact]
    public async Task The_write_path_test_and_the_change_are_both_recorded()
    {
        var controls = await OpenAsync();

        await controls.SetCoolerBoostCommand.ExecuteAsync(true);

        _journal.Snapshot()!.Writes.Should().Equal(
            new Wmi2WriteRecord(Wmi2Feature.PerformanceMode, 0xC1, 0xC1, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.CoolerBoost, 0x00, 0x01, Wmi2ReadBack.Ok));
    }

    [Fact]
    public async Task A_write_that_did_not_read_back_is_recorded_as_such()
    {
        _control.IsWritePathTested = true;
        _control.ApplyOutcome = change => new Wmi2WriteOutcome(
            WriteStatus.FailedRecovered, change.Register, 0xE4, new RegisterWrite(change.Register, 0xD0), Problem: Wmi2GatewayProblem.ReadBackDiffers);
        var controls = await OpenAsync();

        await controls.SetChargeLimitCommand.ExecuteAsync(80);

        _journal.Snapshot()!.Writes.Should().Equal(new Wmi2WriteRecord(Wmi2Feature.ChargeLimit, 0xE4, 0xD0, Wmi2ReadBack.Differs));
    }

    [Fact]
    public async Task A_dry_run_records_nothing()
    {
        _control.TestOutcome = new Wmi2WriteOutcome(WriteStatus.DryRun, 0xD2, 0xC1, new RegisterWrite(0xD2, 0xC1));
        _control.ApplyOutcome = change => new Wmi2WriteOutcome(WriteStatus.DryRun, change.Register, 0x00, new RegisterWrite(change.Register, 0x01));
        var controls = await OpenAsync();

        await controls.SetFanModeCommand.ExecuteAsync(Wmi2FanMode.Silent);

        _journal.Snapshot().Should().BeNull();
    }

    [Fact]
    public async Task Back_to_before_M_Tool_records_what_it_wrote()
    {
        _control.IsWritePathTested = true;
        _control.RevertOutcomes =
        [
            new Wmi2WriteOutcome(WriteStatus.Applied, 0x98, 0x82, new RegisterWrite(0x98, 0x02)),
            new Wmi2WriteOutcome(WriteStatus.Applied, 0xD2, 0xC1),
            new Wmi2WriteOutcome(WriteStatus.Applied, 0xD4, 0x0D),
            new Wmi2WriteOutcome(WriteStatus.Applied, 0xD7, 0xD0, new RegisterWrite(0xD7, 0xE4)),
        ];
        var controls = await OpenAsync();

        await controls.RevertCommand.ExecuteAsync(null);

        _journal.Snapshot()!.Writes.Should().Equal(
            new Wmi2WriteRecord(Wmi2Feature.CoolerBoost, 0x82, 0x02, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.ChargeLimit, 0xD0, 0xE4, Wmi2ReadBack.Ok));
    }

    [Fact]
    public async Task Without_control_nothing_reaches_the_journal()
    {
        await ReadingAsync();

        _journal.Snapshot().Should().BeNull();
    }
}
