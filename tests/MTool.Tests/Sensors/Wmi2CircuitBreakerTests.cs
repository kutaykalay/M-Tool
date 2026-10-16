using MTool.Core.Ec;
using MTool.Core.Sensors;

namespace MTool.Tests.Sensors;

/// <summary>
/// WMI2 reads stop by themselves after failures in a row (CLAUDE.md, "WMI2 canlı okuma"); only the
/// user starts them again. A round ends at its first failure, so the breaker counts finished rounds.
/// </summary>
public class Wmi2CircuitBreakerTests
{
    private int _reads;

    private Task<int> Ok(CancellationToken _)
    {
        _reads++;
        return Task.FromResult(_reads);
    }

    private Task<int> Fail(CancellationToken _)
    {
        _reads++;
        return Task.FromException<int>(new EcAccessException("MSI_ACPI Get_Data(0x68): timeout"));
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_reads()
    {
        var breaker = new Wmi2CircuitBreaker();

        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        var value = await breaker.RunAsync(Ok, default);

        value.Should().Be(2);
        breaker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Two_failures_in_a_row_stop_the_reads_without_another_call()
    {
        var breaker = new Wmi2CircuitBreaker();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();

        var third = () => breaker.RunAsync(Ok, default);

        await third.Should().ThrowAsync<Wmi2StoppedException>().WithMessage("*2*timeout*");
        _reads.Should().Be(2);
        breaker.IsOpen.Should().BeTrue();
        breaker.LastError.Should().Contain("timeout");
    }

    [Fact]
    public async Task A_success_starts_the_count_again()
    {
        var breaker = new Wmi2CircuitBreaker();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        await breaker.RunAsync(Ok, default);
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();

        breaker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Only_reset_closes_it_again()
    {
        var breaker = new Wmi2CircuitBreaker();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Ok, default)).Should().ThrowAsync<Wmi2StoppedException>();

        breaker.Reset();
        var value = await breaker.RunAsync(Ok, default);

        value.Should().Be(3);
        breaker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task A_read_the_sleep_gate_refused_is_not_a_failure()
    {
        var breaker = new Wmi2CircuitBreaker();
        Task<int> Paused(CancellationToken _) => Task.FromException<int>(new EcAccessException("paused") { IsAccessPaused = true });

        for (var i = 0; i < 3; i++)
        {
            await FluentActions.Awaiting(() => breaker.RunAsync(Paused, default)).Should().ThrowAsync<EcAccessException>();
        }

        breaker.IsOpen.Should().BeFalse("sleep is not a broken interface");
    }

    [Fact]
    public async Task A_cancelled_read_is_not_a_failure()
    {
        var breaker = new Wmi2CircuitBreaker();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        for (var i = 0; i < 3; i++)
        {
            await FluentActions.Awaiting(() => breaker.RunAsync(ct => Task.FromCanceled<int>(ct), cancel.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }

        breaker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task A_hung_read_counts_when_it_finally_fails()
    {
        var breaker = new Wmi2CircuitBreaker();
        var hung = new TaskCompletionSource<int>();
        var first = breaker.RunAsync(_ => hung.Task, default);
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        breaker.IsOpen.Should().BeFalse("the hung round has not ended yet");

        hung.SetException(new EcAccessException("MSI_ACPI Get_Data(0xC8): no answer within 2 s (timeout)."));
        await FluentActions.Awaiting(() => first).Should().ThrowAsync<EcAccessException>();

        breaker.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task A_late_success_does_not_close_an_open_breaker()
    {
        var breaker = new Wmi2CircuitBreaker();
        var hung = new TaskCompletionSource<int>();
        var first = breaker.RunAsync(_ => hung.Task, default);
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();

        hung.SetResult(7);
        await first;

        breaker.IsOpen.Should().BeTrue("only the user starts the reads again");
    }

    [Fact]
    public async Task A_round_from_before_reset_does_not_count_after_it()
    {
        var breaker = new Wmi2CircuitBreaker();
        var hung = new TaskCompletionSource<int>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        var old = breaker.RunAsync(_ => hung.Task, default);
        breaker.Reset();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();

        hung.SetException(new EcAccessException("old round"));
        await FluentActions.Awaiting(() => old).Should().ThrowAsync<EcAccessException>();

        breaker.IsOpen.Should().BeFalse("the old round belongs to the session before the user's retry");
        breaker.LastError.Should().NotContain("old round");
    }

    [Fact]
    public async Task Reset_forgets_the_last_error()
    {
        var breaker = new Wmi2CircuitBreaker();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();

        breaker.Reset();

        breaker.LastError.Should().BeNull();
    }

    [Fact]
    public async Task A_read_that_throws_before_returning_a_task_counts()
    {
        var breaker = new Wmi2CircuitBreaker();
        Task<int> Throws(CancellationToken _) => throw new EcAccessException("refused at once");

        await FluentActions.Awaiting(() => breaker.RunAsync(Throws, default)).Should().ThrowAsync<EcAccessException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Throws, default)).Should().ThrowAsync<EcAccessException>();

        breaker.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Sleep_between_two_failures_does_not_clear_the_count()
    {
        var breaker = new Wmi2CircuitBreaker();
        Task<int> Paused(CancellationToken _) => Task.FromException<int>(new EcAccessException("paused") { IsAccessPaused = true });

        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Paused, default)).Should().ThrowAsync<EcAccessException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Fail, default)).Should().ThrowAsync<EcAccessException>();

        breaker.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task A_cancellation_nobody_asked_for_counts()
    {
        var breaker = new Wmi2CircuitBreaker();
        Task<int> Cancelled(CancellationToken _) => Task.FromException<int>(new OperationCanceledException("inner timeout"));

        await FluentActions.Awaiting(() => breaker.RunAsync(Cancelled, default)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => breaker.RunAsync(Cancelled, default)).Should().ThrowAsync<OperationCanceledException>();

        breaker.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void The_threshold_is_the_reports_failures_in_a_row()
    {
        Wmi2CircuitBreaker.MaxFailuresInARow.Should().Be(MTool.Core.Diagnostics.Wmi2Probe.MaxFailuresInARow).And.Be(2);
    }
}
