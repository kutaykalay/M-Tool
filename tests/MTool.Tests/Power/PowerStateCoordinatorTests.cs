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
        var coordinator = new PowerStateCoordinator(new ManualTime(), ResumeDelay);

        coordinator.IsEcAccessAllowed.Should().BeTrue();
    }

    [Fact]
    public void Access_stops_on_suspend()
    {
        var coordinator = new PowerStateCoordinator(new ManualTime(), ResumeDelay);

        coordinator.OnSuspend();

        coordinator.IsEcAccessAllowed.Should().BeFalse();
    }

    [Fact]
    public void Access_resumes_only_after_the_settle_delay()
    {
        var time = new ManualTime();
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
        var time = new ManualTime();
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
        var coordinator = new PowerStateCoordinator(new ManualTime(), ResumeDelay);
        var ran = false;
        using var worker = new EcWorker(
            new FakeEcRegisters(), new FakeEcLock(), TimeSpan.FromMilliseconds(50),
            accessGate: () => coordinator.IsEcAccessAllowed);
        coordinator.OnSuspend();

        var act = () => worker.RunAsync(_ => ran = true);

        await act.Should().ThrowAsync<EcAccessException>().WithMessage("*uyku*");
        ran.Should().BeFalse();
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
