using System.Reflection;
using MTool.Core.Device;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;

namespace MTool.Tests.Diagnostics;

public sealed class Wmi2WriteJournalTests
{
    private const string Firmware = "15M3EMS1.109";

    private static readonly Wmi2Status Status = new(0x02, 0xC1, 0x0D, 0xE4);

    private static Wmi2LayoutResult Layout(Wmi2Status? status = null) => Wmi2LayoutCheck.Check(status ?? Status);

    private static Wmi2WriteJournal Opened(string firmware = Firmware)
    {
        var journal = new Wmi2WriteJournal();
        journal.Opened(firmware, Layout());
        return journal;
    }

    private static Wmi2WriteOutcome Applied(byte register, byte before, byte after) =>
        new(WriteStatus.Applied, register, before, new RegisterWrite(register, after));

    private static Wmi2WriteOutcome Failed(byte register, byte before, byte after, Wmi2GatewayProblem problem, WriteStatus status = WriteStatus.FailedRecovered) =>
        new(status, register, before, new RegisterWrite(register, after), Problem: problem);

    [Fact]
    public void Nothing_is_reported_before_control_opened_or_before_a_write()
    {
        var journal = new Wmi2WriteJournal();
        journal.Record(Applied(0x98, 0x02, 0x82));

        journal.Snapshot().Should().BeNull("control never opened");
        Opened().Snapshot().Should().BeNull("control opened but nothing was written");
    }

    [Fact]
    public void An_applied_write_is_kept_with_its_feature_bytes_and_read_back()
    {
        var journal = Opened();

        journal.Record(Applied(0x98, 0x02, 0x82));
        journal.Record(Applied(0xD2, 0xC1, 0xC4));
        journal.Record(Applied(0xD4, 0x0D, 0x1D));
        journal.Record(Applied(0xD7, 0xE4, 0xD0));

        journal.Snapshot()!.Writes.Should().Equal(
            new Wmi2WriteRecord(Wmi2Feature.CoolerBoost, 0x02, 0x82, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.PerformanceMode, 0xC1, 0xC4, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.FanMode, 0x0D, 0x1D, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.ChargeLimit, 0xE4, 0xD0, Wmi2ReadBack.Ok));
    }

    [Fact]
    public void What_did_not_reach_the_EC_is_not_a_write()
    {
        var journal = Opened();

        journal.Record(new Wmi2WriteOutcome(WriteStatus.Applied, 0xD2, 0xC1)); // already held the value
        journal.Record(new Wmi2WriteOutcome(WriteStatus.DryRun, 0xD2, 0xC1, new RegisterWrite(0xD2, 0xC4)));
        journal.Record(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, 0xC1, Rejection: Wmi2Rejection.NotExactFirmware));
        journal.Record(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD7, Problem: Wmi2GatewayProblem.Unreachable));

        journal.Snapshot().Should().BeNull();
    }

    [Theory]
    [InlineData(Wmi2GatewayProblem.ReadBackDiffers, WriteStatus.FailedRecovered, Wmi2ReadBack.Differs)]
    [InlineData(Wmi2GatewayProblem.RegisterMoved, WriteStatus.FailedRecovered, Wmi2ReadBack.Differs)]
    [InlineData(Wmi2GatewayProblem.WriteFailed, WriteStatus.FailedRecovered, Wmi2ReadBack.Failed)]
    [InlineData(Wmi2GatewayProblem.WriteFailed, WriteStatus.FailedUnrecovered, Wmi2ReadBack.Failed)]
    [InlineData(Wmi2GatewayProblem.ReadBackDiffers, WriteStatus.FailedUnrecovered, Wmi2ReadBack.Differs)]
    public void A_failed_write_says_how_its_read_back_went(Wmi2GatewayProblem problem, WriteStatus status, Wmi2ReadBack expected)
    {
        var journal = Opened();

        journal.Record(Failed(0xD2, 0xC1, 0xC4, problem, status));

        journal.Snapshot()!.Writes.Should().Equal(new Wmi2WriteRecord(Wmi2Feature.PerformanceMode, 0xC1, 0xC4, expected));
    }

    [Fact]
    public void A_register_outside_the_ceiling_is_never_recorded()
    {
        var journal = Opened();

        journal.Record(Applied(0xEB, 0x00, 0x0F));

        journal.Snapshot().Should().BeNull();
    }

    [Fact]
    public void Only_the_last_twenty_writes_are_kept()
    {
        var journal = Opened();
        for (var i = 0; i < 25; i++)
        {
            journal.Record(Applied(0x98, (byte)i, (byte)(i + 1)));
        }

        var writes = journal.Snapshot()!.Writes;

        writes.Should().HaveCount(Wmi2WriteJournal.MaxWrites);
        writes[0].Before.Should().Be(5);
        writes[^1].Before.Should().Be(24);
    }

    /// <summary>The write-path test changes nothing: when the record is full it gives way before a real write does.</summary>
    [Fact]
    public void When_full_a_write_that_changed_nothing_gives_way_first()
    {
        var journal = Opened();
        journal.Record(Applied(0x98, 0x02, 0x82)); // the first real write
        journal.Record(Applied(0xD2, 0xC1, 0xC1)); // the write-path test
        for (var i = 0; i < Wmi2WriteJournal.MaxWrites - 1; i++)
        {
            journal.Record(Applied(0xD7, (byte)(0xC0 + i), (byte)(0xC1 + i)));
        }

        var writes = journal.Snapshot()!.Writes;

        writes.Should().HaveCount(Wmi2WriteJournal.MaxWrites);
        writes[0].Should().Be(new Wmi2WriteRecord(Wmi2Feature.CoolerBoost, 0x02, 0x82, Wmi2ReadBack.Ok));
        writes.Should().NotContain(w => w.Before == w.After);
    }

    [Fact]
    public void The_snapshot_holds_the_firmware_and_the_bytes_the_layout_check_read()
    {
        var journal = Opened();
        journal.Record(Applied(0x98, 0x02, 0x82));

        var session = journal.Snapshot()!;

        session.Firmware.Should().Be(Firmware);
        session.LayoutVerified.Should().BeTrue();
        session.LayoutStatus.Should().Be(Status);
        session.Feedback.Should().BeEmpty();
    }

    [Fact]
    public void Opening_again_for_the_same_firmware_keeps_the_writes_another_firmware_starts_over()
    {
        var journal = Opened();
        journal.Record(Applied(0x98, 0x02, 0x82));

        journal.Opened(Firmware, Layout());
        journal.Snapshot()!.Writes.Should().ContainSingle();

        journal.Opened("15M3EMS1.110", Layout());
        journal.Snapshot().Should().BeNull();
    }

    [Fact]
    public void A_snapshot_does_not_change_after_later_writes()
    {
        var journal = Opened();
        journal.Record(Applied(0x98, 0x02, 0x82));
        var first = journal.Snapshot()!;

        journal.Record(Applied(0x98, 0x82, 0x02));

        first.Writes.Should().ContainSingle();
    }

    [Fact]
    public void Questions_are_one_per_feature_that_changed_in_the_order_first_written()
    {
        var journal = Opened();
        journal.Record(Applied(0xD2, 0xC1, 0xC1)); // the write-path test writes the same byte again
        journal.Record(Applied(0xD7, 0xE4, 0xD0));
        journal.Record(Applied(0x98, 0x02, 0x82));
        journal.Record(Applied(0xD7, 0xD0, 0xE4));
        journal.Record(Failed(0xD4, 0x0D, 0x1D, Wmi2GatewayProblem.WriteFailed));

        journal.Snapshot()!.Questions.Should().Equal(Wmi2Feature.ChargeLimit, Wmi2Feature.CoolerBoost, Wmi2Feature.FanMode);
    }

    [Fact]
    public void A_firmware_or_layout_that_is_missing_is_refused()
    {
        var journal = new Wmi2WriteJournal();

        journal.Invoking(j => j.Opened(null!, Layout())).Should().Throw<ArgumentNullException>();
        journal.Invoking(j => j.Opened(Firmware, null!)).Should().Throw<ArgumentNullException>();
        journal.Invoking(j => j.Record(null!)).Should().Throw<ArgumentNullException>();
    }

    /// <summary>The record lives in memory for the session only: nothing in it can reach a file.</summary>
    [Fact]
    public void The_journal_holds_nothing_that_could_write_a_file()
    {
        var type = typeof(Wmi2WriteJournal);
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        type.GetConstructors().Should().ContainSingle().Which.GetParameters().Should().BeEmpty();
        fields.Select(f => f.FieldType.Namespace).Should().NotContain(ns => ns != null && ns.StartsWith("System.IO", StringComparison.Ordinal));
    }

    /// <summary>The WMI2 window records on the UI thread while "Send EC data" takes snapshots off it.</summary>
    [Fact]
    public void Recording_and_snapshots_at_the_same_time_stay_within_the_limit()
    {
        var journal = Opened();

        Parallel.Invoke(
            () => Parallel.For(0, 500, i => journal.Record(Applied(0x98, (byte)i, (byte)(i + 1)))),
            () =>
            {
                for (var i = 0; i < 500; i++)
                {
                    (journal.Snapshot()?.Writes.Count ?? 0).Should().BeLessThanOrEqualTo(Wmi2WriteJournal.MaxWrites);
                }
            });

        journal.Snapshot()!.Writes.Should().HaveCount(Wmi2WriteJournal.MaxWrites);
    }

    [Fact]
    public void Opening_again_for_the_same_firmware_takes_the_new_layout_check()
    {
        var journal = Opened();
        journal.Record(Applied(0x98, 0x02, 0x82));
        var newer = new Wmi2Status(0x82, 0xC4, 0x0D, 0xD0);

        journal.Opened(Firmware, Layout(newer));

        journal.Snapshot()!.LayoutStatus.Should().Be(newer);
    }
}
