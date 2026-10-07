using Microsoft.Extensions.Time.Testing;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public class EcTroubleLogTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero));
    private readonly ListLog _log = new();

    private EcTroubleLog Create() => new(_log, _time, Interval);

    private static EcTransactionTrouble Trouble(EcOperation operation, byte register, bool succeeded, int failedAttempts) =>
        new(operation, register, succeeded,
            Enumerable.Range(1, failedAttempts)
                .Select(i => new EcFailedAttempt(i, EcFailureKind.NoAnswer, 0x00, []))
                .ToList());

    private static EcTransactionTrouble RecoveredRead(byte register, int failedAttempts = 1) =>
        Trouble(EcOperation.Read, register, succeeded: true, failedAttempts);

    [Fact]
    public void A_failed_read_is_logged_at_once_with_every_attempt()
    {
        var troubleLog = Create();

        troubleLog.Report(Trouble(EcOperation.Read, 0x80, succeeded: false, failedAttempts: 5));

        _log.Lines.Should().ContainSingle().Which.Should()
            .StartWith("WARN EC: Read 0x80 FAILED: #1 NoAnswer").And.Contain("#5 NoAnswer");
    }

    [Fact]
    public void A_recovered_write_is_logged_at_once()
    {
        var troubleLog = Create();

        troubleLog.Report(Trouble(EcOperation.Write, 0xF2, succeeded: true, failedAttempts: 1));

        _log.Lines.Should().ContainSingle().Which.Should().StartWith("WARN EC: Write 0xF2 recovered");
    }

    [Fact]
    public void Recovered_reads_inside_the_interval_write_no_line()
    {
        var troubleLog = Create();

        troubleLog.Report(RecoveredRead(0x68));
        _time.Advance(TimeSpan.FromMinutes(9));
        troubleLog.Report(RecoveredRead(0x71));

        _log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void The_first_recovered_read_after_the_interval_writes_one_summary()
    {
        var troubleLog = Create();

        troubleLog.Report(RecoveredRead(0x68, failedAttempts: 1));
        troubleLog.Report(RecoveredRead(0x80, failedAttempts: 4));
        _time.Advance(TimeSpan.FromMinutes(5));
        troubleLog.Report(RecoveredRead(0x68, failedAttempts: 2));
        _time.Advance(TimeSpan.FromMinutes(5));
        troubleLog.Report(RecoveredRead(0x68, failedAttempts: 1));

        _log.Lines.Should().ContainSingle().Which.Should().Be(
            "INFO EC: reads recovered by retry in the last 10 min: 4 " +
            "(most failed attempts in one read: 4; 0x68×3, 0x80×1).");
    }

    [Fact]
    public void The_summary_names_only_the_five_busiest_registers()
    {
        var troubleLog = Create();

        byte[] registers = [0x68, 0x68, 0x71, 0x71, 0x80, 0x80, 0x89, 0xCA, 0xCC, 0xF2];
        foreach (var register in registers)
        {
            troubleLog.Report(RecoveredRead(register));
        }

        troubleLog.Flush();

        _log.Lines.Should().ContainSingle().Which.Should().EndWith(
            "(most failed attempts in one read: 1; 0x68×2, 0x71×2, 0x80×2, 0x89×1, 0xCA×1, …).");
    }

    [Fact]
    public void Counting_starts_over_after_a_summary()
    {
        var troubleLog = Create();

        troubleLog.Report(RecoveredRead(0x68));
        _time.Advance(Interval);
        troubleLog.Report(RecoveredRead(0x68));
        _time.Advance(TimeSpan.FromMinutes(1));
        troubleLog.Report(RecoveredRead(0x71));
        troubleLog.Flush();

        _log.Lines.Should().HaveCount(2);
        _log.Lines.Last().Should().Be(
            "INFO EC: reads recovered by retry in the last 1 min: 1 (most failed attempts in one read: 1; 0x71×1).");
    }

    [Fact]
    public void Flush_writes_the_pending_summary_once()
    {
        var troubleLog = Create();

        troubleLog.Report(RecoveredRead(0x68));
        troubleLog.Flush();
        troubleLog.Flush();

        _log.Lines.Should().ContainSingle().Which.Should().StartWith("INFO EC: reads recovered by retry in the last 0 min: 1 ");
    }

    [Fact]
    public void Flush_with_nothing_pending_writes_nothing()
    {
        Create().Flush();

        _log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Failures_do_not_count_toward_the_recovered_summary()
    {
        var troubleLog = Create();

        troubleLog.Report(Trouble(EcOperation.Read, 0x68, succeeded: false, failedAttempts: 5));
        troubleLog.Flush();

        _log.Lines.Should().ContainSingle().Which.Should().StartWith("WARN");
    }

    [Fact]
    public void A_non_positive_interval_is_rejected()
    {
        var act = () => new EcTroubleLog(_log, _time, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
