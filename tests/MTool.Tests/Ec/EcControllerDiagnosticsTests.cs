using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public class EcControllerDiagnosticsTests
{
    private static readonly EcProtocolOptions FastOptions = new(MaxAttempts: 3, MaxStatusPolls: 20);

    private readonly List<EcTransactionTrouble> _reports = [];

    private EcController Create(SimulatedEc ec, EcProtocolOptions? options = null) =>
        new(ec, options ?? FastOptions, _reports.Add);

    [Fact]
    public void A_clean_read_reports_nothing()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;

        Create(ec).Read(0x68);

        _reports.Should().BeEmpty();
    }

    [Fact]
    public void A_recovered_read_reports_each_failed_attempt_once_the_read_is_done()
    {
        var ec = new SimulatedEc();
        ec[0x71] = 50;
        ec.DropNextReads(2);

        Create(ec).Read(0x71).Should().Be(50);

        var report = _reports.Should().ContainSingle().Subject;
        report.Operation.Should().Be(EcOperation.Read);
        report.Register.Should().Be(0x71);
        report.Succeeded.Should().BeTrue();
        report.Attempts.Select(a => (a.Attempt, a.Kind)).Should().Equal(
            (1, EcFailureKind.NoAnswer),
            (2, EcFailureKind.NoAnswer));
        report.Attempts.Should().AllSatisfy(a => a.Drained.Should().BeEmpty());
    }

    [Fact]
    public void A_byte_behind_the_answer_is_not_drained_at_once_but_left_to_its_owner_first()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;
        ec.InjectOutputOnNextReadCommand(0x46);

        Create(ec).Read(0x68).Should().Be(60);

        // Nobody collects our late answer (60), so the next attempt drains it as an orphan.
        var attempts = _reports.Should().ContainSingle().Subject.Attempts;
        attempts.Select(a => a.Kind).Should().Equal(EcFailureKind.ExtraByteAfterAnswer, EcFailureKind.OutputPendingAtStart);
        attempts[0].Status.Should().Be(0x01);
        attempts[0].Drained.Should().BeEmpty();
        attempts[1].Drained.Should().Equal(60);
    }

    [Fact]
    public void Stale_output_before_the_command_is_reported_as_pending_output()
    {
        var ec = new SimulatedEc();
        ec[0x68] = 60;
        ec.LeaveStaleOutput(0xAA);

        Create(ec).Read(0x68);

        var failure = _reports.Should().ContainSingle().Subject.Attempts.Should().ContainSingle().Subject;
        failure.Kind.Should().Be(EcFailureKind.OutputPendingAtStart);
        failure.Drained.Should().Equal(0xAA);
    }

    [Fact]
    public void A_hung_ec_is_reported_as_busy_and_named_in_the_exception()
    {
        var ec = new SimulatedEc { Hung = true };

        var act = () => Create(ec, FastOptions with { MaxAttempts = 2 }).Read(0x98);

        act.Should().Throw<EcAccessException>().WithMessage("*0x98*InputBufferBusy*");
        var report = _reports.Should().ContainSingle().Subject;
        report.Succeeded.Should().BeFalse();
        report.Attempts.Should().HaveCount(2).And.AllSatisfy(a =>
        {
            a.Kind.Should().Be(EcFailureKind.InputBufferBusy);
            (a.Status & 0x02).Should().Be(0x02);
        });
    }

    [Fact]
    public void A_failed_write_is_reported_as_a_write()
    {
        var ec = new SimulatedEc { Hung = true };
        IEcWritableRegisters controller = Create(ec, FastOptions with { MaxAttempts = 1 });

        var act = () => controller.Write(0x98, 0x82);

        act.Should().Throw<EcAccessException>();
        var report = _reports.Should().ContainSingle().Subject;
        report.Operation.Should().Be(EcOperation.Write);
        report.Register.Should().Be(0x98);
    }

    [Fact]
    public void A_throwing_trouble_callback_never_fails_the_read()
    {
        var ec = new SimulatedEc();
        ec[0x71] = 50;
        ec.DropNextReads(1);
        var controller = new EcController(ec, FastOptions, _ => throw new InvalidOperationException("log down"));

        controller.Read(0x71).Should().Be(50);
    }
}
