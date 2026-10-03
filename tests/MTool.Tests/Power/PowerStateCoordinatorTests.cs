using Microsoft.Extensions.Time.Testing;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Tests.Fakes;

namespace MTool.Tests.Power;

public class PowerStateCoordinatorTests
{
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(5);

    [Fact]
    public void Access_is_allowed_while_running()
    {
        var coordinator = new PowerStateCoordinator(new FakeTimeProvider(), ResumeDelay);

        coordinator.IsEcAccessAllowed.Should().BeTrue();
    }

    [Fact]
    public void Following_power_events_closes_the_gate_on_sleep_and_opens_it_after_the_delay()
    {
        var time = new FakeTimeProvider();
        var events = new FakePowerEvents();
        var coordinator = new PowerStateCoordinator(time, ResumeDelay);
        using var following = coordinator.Follow(events);

        events.Suspend();
        var asleep = coordinator.IsEcAccessAllowed;
        events.Resume();
        var justAwake = coordinator.IsEcAccessAllowed;
        time.Advance(ResumeDelay);

        asleep.Should().BeFalse();
        justAwake.Should().BeFalse();
        coordinator.IsEcAccessAllowed.Should().BeTrue();
    }

    [Fact]
    public void Disposing_the_follow_stops_listening()
    {
        var events = new FakePowerEvents();
        var coordinator = new PowerStateCoordinator(new FakeTimeProvider(), ResumeDelay);

        coordinator.Follow(events).Dispose();
        events.Suspend();

        events.HasSubscribers.Should().BeFalse();
        coordinator.IsEcAccessAllowed.Should().BeTrue();
    }

    [Fact]
    public void A_wall_clock_step_does_not_open_the_gate_early()
    {
        var time = new SteppedWallClock();
        var coordinator = new PowerStateCoordinator(time, ResumeDelay);
        coordinator.OnSuspend();
        coordinator.OnResume();

        time.WallClockStep = TimeSpan.FromHours(1);
        var afterStep = coordinator.IsEcAccessAllowed;
        time.Advance(ResumeDelay);

        afterStep.Should().BeFalse();
        coordinator.IsEcAccessAllowed.Should().BeTrue();
    }

    [Fact]
    public void Access_stops_on_suspend()
    {
        var coordinator = new PowerStateCoordinator(new FakeTimeProvider(), ResumeDelay);

        coordinator.OnSuspend();

        coordinator.IsEcAccessAllowed.Should().BeFalse();
    }

    [Fact]
    public void Access_resumes_only_after_the_settle_delay()
    {
        var time = new FakeTimeProvider();
        var coordinator = new PowerStateCoordinator(time, ResumeDelay);
        coordinator.OnSuspend();

        coordinator.OnResume();
        time.Advance(ResumeDelay - TimeSpan.FromMilliseconds(1));
        var tooEarly = coordinator.IsEcAccessAllowed;
        time.Advance(TimeSpan.FromMilliseconds(1));

        tooEarly.Should().BeFalse();
        coordinator.IsEcAccessAllowed.Should().BeTrue();
    }

    [Fact]
    public void A_second_suspend_during_the_settle_delay_blocks_again()
    {
        var time = new FakeTimeProvider();
        var coordinator = new PowerStateCoordinator(time, ResumeDelay);
        coordinator.OnSuspend();
        coordinator.OnResume();

        coordinator.OnSuspend();
        time.Advance(ResumeDelay * 2);

        coordinator.IsEcAccessAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task Worker_refuses_ec_access_while_suspended()
    {
        var coordinator = new PowerStateCoordinator(new FakeTimeProvider(), ResumeDelay);
        var ran = false;
        using var worker = new EcWorker(
            new FakeEcRegisters(), new FakeEcLock(), TimeSpan.FromMilliseconds(50),
            accessGate: () => coordinator.IsEcAccessAllowed);
        coordinator.OnSuspend();

        var act = () => worker.RunAsync(_ => ran = true);

        await act.Should().ThrowAsync<EcAccessException>().WithMessage("*uyku*");
        ran.Should().BeFalse();
    }

    /// <summary>Monotonic time from a fake; the wall clock can jump on its own, as after a time sync.</summary>
    private sealed class SteppedWallClock : TimeProvider
    {
        private readonly FakeTimeProvider _monotonic = new();

        public TimeSpan WallClockStep { get; set; }

        public override DateTimeOffset GetUtcNow() => _monotonic.GetUtcNow() + WallClockStep;

        public override long GetTimestamp() => _monotonic.GetTimestamp();

        public override long TimestampFrequency => _monotonic.TimestampFrequency;

        public void Advance(TimeSpan by) => _monotonic.Advance(by);
    }
}
