using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public class EcControllerTests
{
    private static readonly EcProtocolOptions FastOptions = new(MaxAttempts: 5, MaxStatusPolls: 20);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_options_without_any_attempt(int maxAttempts)
    {
        var act = () => new EcController(new SimulatedEc(), FastOptions with { MaxAttempts = maxAttempts });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_lost_answer_is_retried_quickly_with_default_options()
    {
        var ec = new SimulatedEc();
        ec[0xA0] = 0x31;
        ec.DropNextReads(1);
        var controller = new EcController(ec);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        controller.Read(0xA0).Should().Be(0x31);

        // Waiting longer never brought a lost answer back on real hardware; keep retries cheap.
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
        controller.RecoveredFailures.Should().Be(1);
    }

    [Fact]
    public void A_foreign_byte_pending_at_start_is_left_for_its_owner()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;
        ec.LeaveForeignOutput(0x81, collectedAfterPolls: 5);
        var controller = new EcController(ec, FastOptions);

        controller.Read(0x68).Should().Be(60);
        ec.ForeignBytesStolen.Should().Be(0);
        ec.ForeignBytesCollectedByOwner.Should().Be(1);
        controller.RecoveredFailures.Should().Be(0);
    }

    [Fact]
    public void A_foreign_byte_pending_before_a_write_is_left_for_its_owner()
    {
        var ec = new SimulatedEc();
        ec.LeaveForeignOutput(0x81, collectedAfterPolls: 5);
        IEcWritableRegisters controller = new EcController(ec, FastOptions);

        controller.Write(0xEF, 0xCF);

        ec[0xEF].Should().Be(0xCF);
        ec.ForeignBytesStolen.Should().Be(0);
    }

    [Fact]
    public void Read_returns_register_value_without_protocol_violations()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;
        var controller = new EcController(ec);

        var value = controller.Read(0x68);

        value.Should().Be(60);
        ec.ProtocolViolations.Should().Be(0);
        ec.CompletedWrites.Should().Be(0);
    }

    [Fact]
    public void A_late_answer_to_an_abandoned_read_is_never_returned_for_another_register()
    {
        var ec = new SimulatedEc();
        ec[0x80] = 46;
        ec[0x68] = 60;
        var options = new EcProtocolOptions(MaxAttempts: 1, MaxStatusPolls: 20);
        var controller = new EcController(ec, options);
        ec.DelayNextResponse(30);
        Assert.Throws<EcAccessException>(() => controller.Read(0x80));

        var value = new EcController(ec, options with { MaxAttempts = 3 }).Read(0x68);

        value.Should().Be(60);
    }

    [Fact]
    public void A_foreign_byte_that_arrives_mid_transaction_is_discarded()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;
        ec.InjectOutputOnNextReadCommand(0x46);
        var controller = new EcController(ec, new EcProtocolOptions(MaxAttempts: 3, MaxStatusPolls: 20));

        controller.Read(0x68).Should().Be(60);
        // Ambiguous answer, then our own answer left behind is drained as an orphan.
        controller.RecoveredFailures.Should().Be(2);
    }

    [Fact]
    public void Read_reports_failure_when_every_attempt_is_ambiguous()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;
        ec.InjectOutputOnEveryReadCommand(0x46);
        var controller = new EcController(ec, new EcProtocolOptions(MaxAttempts: 2, MaxStatusPolls: 20));

        var act = () => controller.Read(0x68);

        act.Should().Throw<EcAccessException>();
    }

    [Fact]
    public void Read_waits_for_busy_ec_and_delayed_response()
    {
        var ec = new SimulatedEc { BusyPolls = 5, ResponseDelayPolls = 7 };
        ec[0x80] = 46;
        var controller = new EcController(ec);

        controller.Read(0x80).Should().Be(46);
        ec.ProtocolViolations.Should().Be(0);
    }

    [Fact]
    public void Read_retries_when_a_transaction_is_dropped()
    {
        var ec = new SimulatedEc();
        ec[0x71] = 50;
        ec.DropNextReads(2);
        var controller = new EcController(ec, FastOptions);

        controller.Read(0x71).Should().Be(50);
        controller.RecoveredFailures.Should().Be(2);
    }

    [Fact]
    public void Read_throws_after_all_attempts_fail()
    {
        var ec = new SimulatedEc();
        ec.DropNextReads(FastOptions.MaxAttempts);
        var controller = new EcController(ec, FastOptions);

        var act = () => controller.Read(0x68);

        act.Should().Throw<EcAccessException>().WithMessage("*0x68*");
    }

    [Fact]
    public void Read_throws_when_ec_is_hung()
    {
        var ec = new SimulatedEc { Hung = true };
        var controller = new EcController(ec, new EcProtocolOptions(MaxAttempts: 2, MaxStatusPolls: 50));

        var act = () => controller.Read(0x68);

        act.Should().Throw<EcAccessException>();
        ec.CompletedWrites.Should().Be(0);
    }

    [Fact]
    public void Read_drains_stale_output_before_starting()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;
        ec.LeaveStaleOutput(0xAA);
        var controller = new EcController(ec, FastOptions);

        controller.Read(0x68).Should().Be(60);
        ec.ProtocolViolations.Should().Be(0);
    }

    [Fact]
    public void ReadBlock_reads_consecutive_registers()
    {
        var ec = new SimulatedEc();
        ec.Load(0x6A, 55, 64, 70);
        var controller = new EcController(ec);

        controller.ReadBlock(0x6A, 3).Should().Equal(55, 64, 70);
    }

    [Fact]
    public void ReadBlock_rejects_a_range_past_the_last_register()
    {
        var controller = new EcController(new SimulatedEc());

        var act = () => controller.ReadBlock(0xFE, 3);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
