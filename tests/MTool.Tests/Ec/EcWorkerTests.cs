using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public class EcWorkerTests
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Runs_every_operation_on_one_dedicated_thread()
    {
        using var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout);

        var first = await worker.RunAsync(_ => Environment.CurrentManagedThreadId);
        var second = await worker.RunAsync(_ => Environment.CurrentManagedThreadId);

        first.Should().Be(second);
        first.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task Acquires_and_releases_the_lock_on_the_same_thread()
    {
        var ecLock = new FakeEcLock();
        using var worker = new EcWorker(new FakeEcRegisters(), ecLock, LockTimeout);

        await worker.RunAsync(ec => ec.Read(0x68));

        var events = ecLock.Events.ToArray();
        events.Select(e => e.Action).Should().Equal("acquire", "release");
        events[0].ThreadId.Should().Be(events[1].ThreadId);
        ecLock.Held.Should().BeFalse();
    }

    [Fact]
    public async Task Passes_the_registers_to_the_operation()
    {
        var registers = new FakeEcRegisters();
        registers.Load(0x68, 60);
        using var worker = new EcWorker(registers, new FakeEcLock(), LockTimeout);

        var value = await worker.RunAsync(ec => ec.Read(0x68));

        value.Should().Be(60);
    }

    [Fact]
    public async Task Fails_without_running_the_operation_when_the_lock_is_busy()
    {
        var ran = false;
        using var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock { Available = false }, LockTimeout);

        var act = () => worker.RunAsync(_ => ran = true);

        (await act.Should().ThrowAsync<EcAccessException>()).Which.IsAccessPaused.Should().BeFalse();
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task A_closed_access_gate_fails_as_paused_without_touching_the_ec()
    {
        var ran = false;
        var ecLock = new FakeEcLock();
        using var worker = new EcWorker(new FakeEcRegisters(), ecLock, LockTimeout, accessGate: () => false);

        var act = () => worker.RunAsync(_ => ran = true);

        (await act.Should().ThrowAsync<EcAccessException>()).Which.IsAccessPaused.Should().BeTrue();
        ran.Should().BeFalse();
        ecLock.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Releases_the_lock_and_surfaces_the_error_when_the_operation_throws()
    {
        var ecLock = new FakeEcLock();
        using var worker = new EcWorker(new FakeEcRegisters(), ecLock, LockTimeout);

        var act = () => worker.RunAsync<int>(_ => throw new EcAccessException("boom"));

        await act.Should().ThrowAsync<EcAccessException>().WithMessage("boom");
        ecLock.Held.Should().BeFalse();
    }

    [Fact]
    public async Task Keeps_working_after_a_failed_operation()
    {
        using var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.RunAsync<int>(_ => throw new InvalidOperationException()));

        var value = await worker.RunAsync(_ => 7);

        value.Should().Be(7);
    }

    [Fact]
    public async Task Executes_operations_in_submission_order()
    {
        var order = new List<int>();
        using var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout);

        var tasks = Enumerable.Range(0, 20)
            .Select(i => worker.RunAsync(_ => { order.Add(i); return i; }))
            .ToArray();
        await Task.WhenAll(tasks);

        order.Should().Equal(Enumerable.Range(0, 20));
    }

    [Fact]
    public async Task A_throwing_lock_faults_the_task_and_the_worker_survives()
    {
        var ecLock = new FakeEcLock { AcquireError = new UnauthorizedAccessException("acl") };
        using var worker = new EcWorker(new FakeEcRegisters(), ecLock, LockTimeout);

        var act = () => worker.RunAsync(_ => 1);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        ecLock.AcquireError = null;
        (await worker.RunAsync(_ => 2)).Should().Be(2);
    }

    [Fact]
    public async Task A_throwing_release_does_not_hide_the_result_or_kill_the_worker()
    {
        var ecLock = new FakeEcLock { ReleaseError = new ApplicationException("not owned") };
        using var worker = new EcWorker(new FakeEcRegisters(), ecLock, LockTimeout);

        (await worker.RunAsync(_ => 1)).Should().Be(1);
        (await worker.RunAsync(_ => 2)).Should().Be(2);
    }

    [Fact]
    public async Task Dispose_fails_work_that_has_not_started()
    {
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout);
        var running = worker.RunAsync(_ =>
        {
            started.Set();
            return gate.Wait(TimeSpan.FromSeconds(5));
        });
        var pending = worker.RunAsync(_ => 2);

        // Dispose before the first operation began would fail it too, as work not yet started.
        started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        // Open the gate only once Dispose is blocked in Join, i.e. after it marked the worker
        // disposed; a fixed delay was flaky when the thread pool was busy with other tests.
        var disposer = new Thread(worker.Dispose);
        disposer.Start();
        SpinWait.SpinUntil(() => disposer.ThreadState.HasFlag(ThreadState.WaitSleepJoin), TimeSpan.FromSeconds(5))
            .Should().BeTrue();
        gate.Set();
        disposer.Join();

        (await running).Should().BeTrue();
        await pending.Invoking(t => t).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_can_be_called_concurrently()
    {
        var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout);

        var act = () => Parallel.For(0, 8, _ => worker.Dispose());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Cancelled_work_does_not_run()
    {
        var ran = false;
        using var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => worker.RunAsync(_ => ran = true, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_work_after_dispose()
    {
        var worker = new EcWorker(new FakeEcRegisters(), new FakeEcLock(), LockTimeout);
        worker.Dispose();

        var act = () => worker.RunAsync(_ => 1);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
