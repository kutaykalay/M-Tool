using Microsoft.Extensions.Time.Testing;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Tests.Fakes;

namespace MTool.Tests.Power;

public sealed class AutoReapplierTests : IDisposable
{
    private static readonly AutoReapplyOptions Options = AutoReapplyOptions.Default;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 22, 0, 0, TimeSpan.Zero));
    private readonly FakePowerEvents _events = new();
    private readonly ListLog _log = new();
    private readonly PowerStateCoordinator _coordinator;
    private readonly Queue<Func<Task<IReadOnlyList<WriteOutcome>>>> _answers = new();
    private readonly List<Call> _calls = [];
    private readonly List<AutoReapplyResult> _results = [];
    private AutoReapplier? _reapplier;

    public AutoReapplierTests() => _coordinator = new PowerStateCoordinator(_time, Options.ResumeDelay);

    public void Dispose() => _reapplier?.Dispose();

    private AutoReapplier Reapplier(AutoReapplyOptions? options = null)
    {
        _reapplier = new AutoReapplier(Reapply, _events, _coordinator, _time, _log, options ?? Options);
        _reapplier.Reapplied += _results.Add;
        return _reapplier;
    }

    private AutoReapplier Started()
    {
        var reapplier = Reapplier();
        reapplier.Start();
        return reapplier;
    }

    private Task<IReadOnlyList<WriteOutcome>> Reapply(PortUse portUse)
    {
        _calls.Add(new Call(portUse, _coordinator.IsEcAccessAllowed));
        return _answers.Count > 0 ? _answers.Dequeue()() : Task.FromResult(Outcomes(WriteStatus.Applied));
    }

    private void Answer(params WriteStatus[] statuses) =>
        _answers.Enqueue(() => Task.FromResult(Outcomes(statuses)));

    private void Throw(Exception error) =>
        _answers.Enqueue(() => Task.FromException<IReadOnlyList<WriteOutcome>>(error));

    private TaskCompletionSource<IReadOnlyList<WriteOutcome>> Hold()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<WriteOutcome>>();
        _answers.Enqueue(() => pending.Task);
        return pending;
    }

    private static IReadOnlyList<WriteOutcome> Outcomes(params WriteStatus[] statuses) =>
        [.. statuses.Select(s => new WriteOutcome(s, [], s.ToString()))];

    private void SleepAndWake()
    {
        _events.Suspend();
        _events.Resume();
    }

    private sealed record Call(PortUse PortUse, bool GateOpen);

    private sealed class ThrowingLog : MTool.Core.IAppLog
    {
        public void Info(string message) => throw new IOException("log");

        public void Warn(string message) => throw new IOException("log");

        public void Error(string message, Exception? exception = null) => throw new IOException("log");
    }

    // --- start-up ---

    [Fact]
    public void Start_reapplies_once_right_away()
    {
        Started();

        _calls.Should().ContainSingle();
        _results.Should().ContainSingle().Which.Trigger.Should().Be(ReapplyTrigger.Startup);
    }

    [Fact]
    public void Start_twice_reapplies_once()
    {
        var reapplier = Started();

        reapplier.Start();

        _calls.Should().ContainSingle();
    }

    [Fact]
    public void Nothing_happens_before_start()
    {
        Reapplier();

        SleepAndWake();
        _time.Advance(TimeSpan.FromMinutes(1));

        _calls.Should().BeEmpty();
    }

    [Fact]
    public void Automatic_reapplying_never_uses_the_port()
    {
        Started();
        SleepAndWake();
        _time.Advance(Options.ResumeDelay);

        _calls.Should().HaveCount(2).And.OnlyContain(c => c.PortUse == PortUse.None);
    }

    // --- sleep and resume ---

    [Fact]
    public void Suspend_closes_the_gate()
    {
        Started();

        _events.Suspend();

        _coordinator.IsEcAccessAllowed.Should().BeFalse();
    }

    [Fact]
    public void Resume_reapplies_only_after_the_delay()
    {
        Started();
        SleepAndWake();

        _time.Advance(Options.ResumeDelay - TimeSpan.FromMilliseconds(1));
        var tooEarly = _calls.Count;
        _time.Advance(TimeSpan.FromMilliseconds(1));

        tooEarly.Should().Be(1);
        _calls.Should().HaveCount(2);
        _calls[^1].GateOpen.Should().BeTrue();
        _results[^1].Trigger.Should().Be(ReapplyTrigger.Resume);
    }

    [Fact]
    public void Two_resumes_in_a_row_reapply_once()
    {
        Started();
        _events.Suspend();
        _events.Resume();
        _time.Advance(TimeSpan.FromSeconds(1));

        _events.Resume();
        _time.Advance(TimeSpan.FromMinutes(1));

        _calls.Should().HaveCount(2);
        _calls[^1].GateOpen.Should().BeTrue();
    }

    [Fact]
    public void Suspend_while_waiting_cancels_the_reapply()
    {
        Started();
        SleepAndWake();
        _time.Advance(TimeSpan.FromSeconds(2));

        _events.Suspend();
        _time.Advance(TimeSpan.FromMinutes(1));

        _calls.Should().ContainSingle();
    }

    [Fact]
    public void The_next_resume_schedules_again_after_a_cancelled_one()
    {
        Started();
        SleepAndWake();
        _events.Suspend();

        _events.Resume();
        _time.Advance(Options.ResumeDelay);

        _calls.Should().HaveCount(2);
    }

    [Fact]
    public void A_closed_gate_at_run_time_defers_the_reapply_to_one_retry()
    {
        Reapplier(Options with { ResumeDelay = TimeSpan.FromSeconds(1) }).Start();
        SleepAndWake();

        _time.Advance(TimeSpan.FromSeconds(1));
        var whileClosed = _calls.Count;
        _time.Advance(Options.RetryDelay);

        whileClosed.Should().Be(1);
        _log.Lines.Should().Contain(l => l.StartsWith("WARN"));
        _calls.Should().HaveCount(2);
        _calls[^1].GateOpen.Should().BeTrue();
        _results[^1].Trigger.Should().Be(ReapplyTrigger.Retry);
    }

    [Fact]
    public void A_resume_without_a_suspend_still_reapplies_once()
    {
        Started();

        _events.Resume();
        _time.Advance(Options.ResumeDelay);

        _calls.Should().HaveCount(2);
    }

    [Fact]
    public void A_resume_during_a_reapply_drops_its_retry_and_reapplies_once_later()
    {
        var pending = Hold();
        Started();

        _events.Resume();
        pending.SetResult(Outcomes(WriteStatus.Rejected));
        _time.Advance(Options.ResumeDelay);
        _time.Advance(TimeSpan.FromMinutes(5));

        _results.Select(r => r.Trigger).Should().Equal(ReapplyTrigger.Startup, ReapplyTrigger.Resume);
    }

    // --- retry ---

    [Fact]
    public void Rejected_is_retried_once_after_the_retry_delay()
    {
        Answer(WriteStatus.Rejected);
        Started();

        _time.Advance(Options.RetryDelay - TimeSpan.FromMilliseconds(1));
        var tooEarly = _calls.Count;
        _time.Advance(TimeSpan.FromMilliseconds(1));

        tooEarly.Should().Be(1);
        _calls.Should().HaveCount(2).And.OnlyContain(c => c.PortUse == PortUse.None);
        _results.Select(r => r.Trigger).Should().Equal(ReapplyTrigger.Startup, ReapplyTrigger.Retry);
    }

    [Fact]
    public void A_rejected_retry_is_not_retried()
    {
        Answer(WriteStatus.Rejected);
        Answer(WriteStatus.Rejected);
        Started();

        _time.Advance(Options.RetryDelay);
        _time.Advance(TimeSpan.FromMinutes(5));

        _calls.Should().HaveCount(2);
    }

    [Fact]
    public void Rejected_after_an_applied_part_is_retried()
    {
        Answer(WriteStatus.Applied, WriteStatus.Rejected);
        Started();

        _time.Advance(Options.RetryDelay);

        _calls.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(WriteStatus.Applied)]
    [InlineData(WriteStatus.DryRun)]
    [InlineData(WriteStatus.FailedRecovered)]
    [InlineData(WriteStatus.FailedUnrecovered)]
    public void Other_outcomes_are_not_retried(WriteStatus status)
    {
        Answer(status);
        Started();

        _time.Advance(TimeSpan.FromMinutes(5));

        _calls.Should().ContainSingle();
    }

    [Fact]
    public void Nothing_attempted_is_not_retried()
    {
        Answer();
        Started();

        _time.Advance(TimeSpan.FromMinutes(5));

        _calls.Should().ContainSingle();
    }

    [Fact]
    public void Suspend_cancels_a_waiting_retry()
    {
        Answer(WriteStatus.Rejected);
        Started();

        _events.Suspend();
        _time.Advance(TimeSpan.FromMinutes(5));

        _calls.Should().ContainSingle();
    }

    [Fact]
    public void Suspend_during_a_reapply_drops_its_retry_but_still_reports_it()
    {
        var pending = Hold();
        Started();

        _events.Suspend();
        pending.SetResult(Outcomes(WriteStatus.Rejected));
        _time.Advance(TimeSpan.FromMinutes(5));

        _calls.Should().ContainSingle();
        _results.Should().ContainSingle();
    }

    [Fact]
    public void A_resumed_reapply_that_is_rejected_is_retried()
    {
        Started();
        Answer(WriteStatus.Rejected);
        SleepAndWake();

        _time.Advance(Options.ResumeDelay);
        _time.Advance(Options.RetryDelay);

        _results.Select(r => r.Trigger)
            .Should().Equal(ReapplyTrigger.Startup, ReapplyTrigger.Resume, ReapplyTrigger.Retry);
    }

    // --- errors ---

    [Fact]
    public void An_exception_is_logged_and_does_not_escape()
    {
        Throw(new InvalidOperationException("boom"));

        var start = () => Started();

        start.Should().NotThrow();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("boom"));
        _results.Should().BeEmpty();
    }

    [Fact]
    public void An_exception_is_not_retried_and_the_next_resume_still_runs()
    {
        Throw(new InvalidOperationException("boom"));
        Started();
        _time.Advance(TimeSpan.FromMinutes(5));

        SleepAndWake();
        _time.Advance(Options.ResumeDelay);

        _calls.Should().HaveCount(2);
        _results.Should().ContainSingle().Which.Trigger.Should().Be(ReapplyTrigger.Resume);
    }

    [Fact]
    public void A_failing_subscriber_is_logged_and_does_not_escape()
    {
        var reapplier = Reapplier();
        reapplier.Reapplied += _ => throw new InvalidOperationException("subscriber");

        var start = reapplier.Start;

        start.Should().NotThrow();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("subscriber"));
    }

    [Fact]
    public void A_failing_log_does_not_escape_either()
    {
        Throw(new InvalidOperationException("boom"));
        var reapplier = new AutoReapplier(Reapply, _events, _coordinator, _time, new ThrowingLog(), Options);

        var start = reapplier.Start;

        start.Should().NotThrow();
        reapplier.Dispose();
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(5, 0)]
    [InlineData(-1, 30)]
    public void Delays_must_be_positive(int resumeSeconds, int retrySeconds)
    {
        var options = new AutoReapplyOptions(TimeSpan.FromSeconds(resumeSeconds), TimeSpan.FromSeconds(retrySeconds));

        var create = () => new AutoReapplier(Reapply, _events, _coordinator, _time, _log, options);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_result_carries_the_outcomes()
    {
        Answer(WriteStatus.Applied, WriteStatus.DryRun);

        Started();

        _results.Should().ContainSingle()
            .Which.Outcomes.Select(o => o.Status).Should().Equal(WriteStatus.Applied, WriteStatus.DryRun);
    }

    // --- dispose ---

    [Fact]
    public void Dispose_unsubscribes_and_cancels_waiting_work()
    {
        var reapplier = Started();
        SleepAndWake();

        reapplier.Dispose();
        _time.Advance(TimeSpan.FromMinutes(5));

        _events.HasSubscribers.Should().BeFalse();
        _calls.Should().ContainSingle();
    }

    [Fact]
    public void A_reapply_finishing_after_dispose_is_not_reported()
    {
        var pending = Hold();
        var reapplier = Started();

        reapplier.Dispose();
        pending.SetResult(Outcomes(WriteStatus.Rejected));
        _time.Advance(TimeSpan.FromMinutes(5));

        _results.Should().BeEmpty();
        _calls.Should().ContainSingle();
    }

    [Fact]
    public void Start_after_dispose_does_nothing()
    {
        var reapplier = Reapplier();
        reapplier.Dispose();

        reapplier.Start();

        _calls.Should().BeEmpty();
        _events.HasSubscribers.Should().BeFalse();
    }

    [Fact]
    public void Dispose_twice_is_harmless()
    {
        var reapplier = Started();
        reapplier.Dispose();

        var again = reapplier.Dispose;

        again.Should().NotThrow();
    }
}
