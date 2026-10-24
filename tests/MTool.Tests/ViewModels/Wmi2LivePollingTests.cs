using Microsoft.Extensions.Time.Testing;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device.Config;
using MTool.Core.Sensors;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>The periodic part of the live values: sensors only, while the window is shown, through the same 2 s rule.</summary>
public class Wmi2LivePollingTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private readonly FakeWmi2LiveSource _source = new();
    private readonly FakeConfirm _confirm = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLog _log = new();
    private readonly Wmi2CircuitBreaker _breaker = new();
    private bool _gateOpen = true;

    private Wmi2LiveViewModel Create() => new(() => _source, _breaker, _confirm, () => _gateOpen, _time, _log, WmiInterface.Wmi2, list: null);

    private async Task<Wmi2LiveViewModel> ShownAndStartedAsync()
    {
        var live = Create();
        live.SetWindowShown(true);
        await live.ShowCommand.ExecuteAsync(null);
        return live;
    }

    private void Tick() => _time.Advance(Wmi2LiveViewModel.PollInterval);

    /// <summary>Lets a tick's round (on a pool thread) run; for checks that nothing was read.</summary>
    private static void Pause() => Thread.Sleep(100);

    [Fact]
    public void The_poll_interval_is_never_shorter_than_the_read_policy() =>
        Wmi2LiveViewModel.PollInterval.Should().BeGreaterThanOrEqualTo(Wmi2ReadPolicy.MinInterval);

    [Fact]
    public async Task While_shown_each_tick_reads_the_sensors_only()
    {
        using var live = await ShownAndStartedAsync();

        Tick();
        SpinWait.SpinUntil(() => _source.SensorReads == 2, Settle).Should().BeTrue();
        Tick();
        SpinWait.SpinUntil(() => _source.SensorReads == 3, Settle).Should().BeTrue();

        (_source.FirmwareReads, _source.StatusReads).Should().Be((1, 1));
        live.IsPolling.Should().BeTrue();
        _confirm.Questions.Should().HaveCount(1);
    }

    [Fact]
    public async Task Nothing_is_read_before_the_interval()
    {
        using var live = await ShownAndStartedAsync();

        _time.Advance(Wmi2ReadPolicy.MinInterval);
        Pause();

        _source.SensorReads.Should().Be(1);
    }

    [Fact]
    public async Task Without_the_user_saying_yes_the_timer_reads_nothing()
    {
        _confirm.Answer = false;
        using var live = await ShownAndStartedAsync();

        Tick();
        Tick();
        Pause();

        _source.Reads.Should().Be(0);
        live.IsPolling.Should().BeFalse();
    }

    [Fact]
    public async Task A_window_that_was_never_shown_is_not_polled()
    {
        using var live = Create();
        await live.ShowCommand.ExecuteAsync(null);

        Tick();
        Pause();

        _source.SensorReads.Should().Be(1);
        live.IsPolling.Should().BeFalse();
    }

    [Fact]
    public async Task Hidden_or_minimised_reads_nothing_and_shown_again_goes_on_without_asking()
    {
        using var live = await ShownAndStartedAsync();

        live.SetWindowShown(false);
        Tick();
        Tick();
        Pause();
        _source.SensorReads.Should().Be(1);
        live.IsPolling.Should().BeFalse();

        live.SetWindowShown(true);
        Tick();

        SpinWait.SpinUntil(() => _source.SensorReads == 2, Settle).Should().BeTrue();
        _confirm.Questions.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_refresh_just_before_a_tick_makes_that_tick_read_nothing()
    {
        using var live = await ShownAndStartedAsync();
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await live.RefreshCommand.ExecuteAsync(null);
        var reads = _source.SensorReads;

        _time.Advance(Wmi2LiveViewModel.PollInterval - Wmi2ReadPolicy.MinInterval); // the first tick, 0.25 s after the refresh
        Pause();

        _source.SensorReads.Should().Be(reads);
    }

    [Fact]
    public async Task Failures_in_a_row_open_the_breaker_and_stop_the_timer()
    {
        using var live = await ShownAndStartedAsync();
        _source.Error = FakeWmi2LiveSource.Timeout();

        for (var i = 0; i < Wmi2CircuitBreaker.MaxFailuresInARow; i++)
        {
            var reads = _source.SensorReads;
            Tick();
            SpinWait.SpinUntil(() => _source.SensorReads == reads + 1 && !live.IsBusy, Settle).Should().BeTrue();
        }

        SpinWait.SpinUntil(() => live.IsStopped && !live.IsPolling, Settle).Should().BeTrue();
        live.Message.Should().Be(Strings.Wmi2_Stopped);
        _log.Lines.Count(line => line.Contains("breaker open", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task Try_again_starts_the_timer_again()
    {
        using var live = await ShownAndStartedAsync();
        _source.Error = FakeWmi2LiveSource.Timeout();
        for (var i = 0; i < Wmi2CircuitBreaker.MaxFailuresInARow; i++)
        {
            var reads = _source.SensorReads;
            Tick();
            SpinWait.SpinUntil(() => _source.SensorReads == reads + 1 && !live.IsBusy, Settle).Should().BeTrue();
        }

        SpinWait.SpinUntil(() => !live.IsPolling, Settle).Should().BeTrue();
        _source.Error = null;
        _time.Advance(Wmi2ReadPolicy.MinInterval); // try again waits for the 2 s rule too
        await live.TryAgainCommand.ExecuteAsync(null);
        var after = _source.SensorReads;
        Tick();

        live.IsPolling.Should().BeTrue();
        SpinWait.SpinUntil(() => _source.SensorReads == after + 1, Settle).Should().BeTrue();
    }

    [Fact]
    public async Task While_the_gate_is_closed_ticks_read_nothing_and_say_paused_then_go_on()
    {
        using var live = await ShownAndStartedAsync();

        _gateOpen = false;
        Tick();
        SpinWait.SpinUntil(() => live.Message == Strings.Sensors_Paused, Settle).Should().BeTrue();
        _source.SensorReads.Should().Be(1);
        live.IsPolling.Should().BeTrue();

        _gateOpen = true;
        Tick();
        SpinWait.SpinUntil(() => _source.SensorReads == 2 && live.Message is null, Settle).Should().BeTrue();
        _breaker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_closes_the_interface_reads_nothing_more_and_asks_again_on_show()
    {
        using var live = await ShownAndStartedAsync();
        live.StopCommand.CanExecute(null).Should().BeTrue();

        live.StopCommand.Execute(null);
        Tick();
        Tick();
        Pause();

        _source.Disposed.Should().BeTrue();
        _source.SensorReads.Should().Be(1);
        (live.IsStarted, live.IsPolling).Should().Be((false, false));
        live.CpuTemperature.Should().Be("—");
        live.ShowCommand.CanExecute(null).Should().BeTrue();
        _log.Lines.Should().Contain(line => line.Contains("stopped by the user", StringComparison.Ordinal));

        await live.ShowCommand.ExecuteAsync(null);
        _confirm.Questions.Should().HaveCount(2);
    }

    [Fact]
    public void Stop_is_offered_only_while_started() =>
        Create().StopCommand.CanExecute(null).Should().BeFalse();

    [Fact]
    public async Task After_closing_the_timer_reads_nothing()
    {
        var live = await ShownAndStartedAsync();

        live.Dispose();
        Tick();
        Tick();
        Pause();

        _source.SensorReads.Should().Be(1);
        live.IsPolling.Should().BeFalse();
    }

    [Fact]
    public async Task Ticks_never_overlap_a_round_still_running()
    {
        using var live = await ShownAndStartedAsync();
        _source.HoldSensors = new TaskCompletionSource();
        Tick();
        SpinWait.SpinUntil(() => _source.SensorReads == 2, Settle).Should().BeTrue();

        Tick();
        Tick();
        Pause();
        _source.SensorReads.Should().Be(2);

        _source.HoldSensors.SetResult();
        SpinWait.SpinUntil(() => !live.IsBusy, Settle).Should().BeTrue();
    }
    [Fact]
    public async Task A_tick_during_a_refresh_that_hangs_past_two_seconds_reads_nothing()
    {
        using var live = await ShownAndStartedAsync();
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        _source.HoldStatus = new TaskCompletionSource();
        var refresh = live.RefreshCommand.ExecuteAsync(null);
        var reads = _source.Reads;

        Tick();
        Tick();
        Pause();
        _source.Reads.Should().Be(reads);

        _source.HoldStatus.SetResult();
        await refresh;
    }

    [Fact]
    public async Task Shown_again_while_stopped_does_not_start_the_timer()
    {
        using var live = await ShownAndStartedAsync();
        _source.Error = FakeWmi2LiveSource.Timeout();
        for (var i = 0; i < Wmi2CircuitBreaker.MaxFailuresInARow; i++)
        {
            var reads = _source.SensorReads;
            Tick();
            SpinWait.SpinUntil(() => _source.SensorReads == reads + 1 && !live.IsBusy, Settle).Should().BeTrue();
        }

        SpinWait.SpinUntil(() => !live.IsPolling, Settle).Should().BeTrue();
        live.SetWindowShown(false);
        live.SetWindowShown(true);

        live.IsPolling.Should().BeFalse();
    }

    [Fact]
    public async Task Shown_again_after_closing_does_not_start_the_timer()
    {
        var live = await ShownAndStartedAsync();
        live.Dispose();

        live.SetWindowShown(true);

        live.IsPolling.Should().BeFalse();
    }
    [Fact]
    public async Task Show_within_two_seconds_of_the_last_round_reads_everything_once_they_have_passed()
    {
        using var live = await ShownAndStartedAsync();
        live.StopCommand.Execute(null);
        var shown = live.ShowCommand.ExecuteAsync(null); // within 2 s of the last round: waits
        Pause();
        (_source.FirmwareReads, _source.StatusReads).Should().Be((1, 1));
        live.Firmware.Should().Be("—");

        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await shown.WaitAsync(Settle);

        (_source.FirmwareReads, _source.StatusReads).Should().Be((2, 2));
        live.Firmware.Should().Be("16Q4EMS2.107");
        live.IsPolling.Should().BeTrue();
    }

    [Fact]
    public async Task Closing_while_show_waits_for_the_two_seconds_reads_nothing()
    {
        var live = await ShownAndStartedAsync();
        live.StopCommand.Execute(null);
        var shown = live.ShowCommand.ExecuteAsync(null);

        live.Dispose();
        await shown.WaitAsync(Settle);
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        Pause();

        _source.Reads.Should().Be(3); // the first Show's round only
        live.IsPolling.Should().BeFalse();
    }

    [Fact]
    public async Task A_round_cut_short_by_closing_is_not_logged_as_a_failure()
    {
        var live = await ShownAndStartedAsync();
        _source.HoldStatus = new TaskCompletionSource();
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        var refresh = live.RefreshCommand.ExecuteAsync(null);

        live.Dispose();
        _source.HoldStatus.SetException(new ObjectDisposedException("EcWorker"));
        await refresh.WaitAsync(Settle);

        _log.Lines.Should().NotContain(line => line.StartsWith("WARN") && line.Contains("failed"));
    }
}
