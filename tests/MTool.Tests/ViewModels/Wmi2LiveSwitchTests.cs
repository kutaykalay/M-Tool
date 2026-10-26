using Microsoft.Extensions.Time.Testing;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Sensors;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

/// <summary>
/// Switching the live values to another source (plan asama15 15c-4/15c-5): the read-only source is closed
/// first, the other one opened, and the timer waits in between. When the other one does not open, the
/// read-only source opens again. The 2 s rule holds on both sides of the switch.
/// </summary>
public class Wmi2LiveSwitchTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private readonly FakeWmi2LiveSource _readOnly = new();
    private readonly FakeWmi2LiveSource _other = new() { Firmware = "15M3EMS1.105" };
    private readonly FakeConfirm _confirm = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLog _log = new();
    private int _opened;

    private Wmi2LiveViewModel Create() =>
        new(() =>
        {
            _opened++;
            return _readOnly;
        }, new Wmi2CircuitBreaker(), _confirm, () => true, _time, _log, WmiInterface.Wmi2, list: null);

    private async Task<Wmi2LiveViewModel> StartedAsync()
    {
        var live = Create();
        live.SetWindowShown(true);
        await live.ShowCommand.ExecuteAsync(null);
        return live;
    }

    /// <summary>Starts a switch, lets the 2 s rule pass, and waits for it.</summary>
    private async Task SwitchAsync(Wmi2LiveViewModel live, Func<CancellationToken, Task<IWmi2LiveSource?>> openOther)
    {
        var switching = live.SwitchSourceAsync(openOther);
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await switching.WaitAsync(Settle);
    }

    [Fact]
    public async Task The_read_only_source_is_closed_before_the_other_one_opens()
    {
        using var live = await StartedAsync();
        bool? closedFirst = null;

        await SwitchAsync(live, _ =>
        {
            closedFirst = _readOnly.Disposed;
            return Task.FromResult<IWmi2LiveSource?>(_other);
        });

        closedFirst.Should().BeTrue();
        live.Source.Should().BeSameAs(_other);
        live.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task After_the_switch_the_rounds_read_through_the_new_source()
    {
        using var live = await StartedAsync();

        await SwitchAsync(live, _ => Task.FromResult<IWmi2LiveSource?>(_other));
        _time.Advance(Wmi2LiveViewModel.PollInterval);

        SpinWait.SpinUntil(() => _other.SensorReads == 1, Settle).Should().BeTrue();
        _other.StatusReads.Should().Be(1, "the first round after a switch reads the status again");
        live.IsPolling.Should().BeTrue();
    }

    [Fact]
    public async Task The_switch_waits_for_the_2_s_rule_before_it_opens()
    {
        using var live = await StartedAsync();
        var opens = 0;

        var switching = live.SwitchSourceAsync(_ =>
        {
            opens++;
            return Task.FromResult<IWmi2LiveSource?>(_other);
        });
        _time.Advance(Wmi2ReadPolicy.MinInterval - TimeSpan.FromMilliseconds(1));
        Thread.Sleep(100);

        opens.Should().Be(0);
        _readOnly.Disposed.Should().BeFalse();
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await switching.WaitAsync(Settle);
        opens.Should().Be(1);
    }

    [Fact]
    public async Task Opening_the_other_source_counts_as_a_round_for_the_2_s_rule()
    {
        using var live = await StartedAsync();
        await SwitchAsync(live, _ => Task.FromResult<IWmi2LiveSource?>(_other));

        await live.RefreshCommand.ExecuteAsync(null);

        _other.Reads.Should().Be(0, "the opening read the EC a moment ago");
    }

    [Fact]
    public async Task When_the_other_source_does_not_open_the_read_only_one_opens_again()
    {
        using var live = await StartedAsync();

        await SwitchAsync(live, _ => Task.FromResult<IWmi2LiveSource?>(null));

        _opened.Should().Be(2);
        live.Source.Should().BeSameAs(_readOnly);
        live.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task When_opening_the_other_source_throws_the_read_only_one_opens_again_and_it_is_logged()
    {
        using var live = await StartedAsync();

        await SwitchAsync(live, _ => Task.FromException<IWmi2LiveSource?>(new InvalidOperationException("boom")));

        _opened.Should().Be(2);
        live.Source.Should().BeSameAs(_readOnly);
        _log.Lines.Should().Contain(line => line.StartsWith("WARN", StringComparison.Ordinal) && line.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task When_neither_opens_reading_is_unavailable()
    {
        var calls = 0;
        var live = new Wmi2LiveViewModel(
            () => ++calls == 1 ? _readOnly : null, new Wmi2CircuitBreaker(), _confirm, () => true, _time, _log, WmiInterface.Wmi2, list: null);
        await live.ShowCommand.ExecuteAsync(null);

        await SwitchAsync(live, _ => Task.FromResult<IWmi2LiveSource?>(null));

        live.Source.Should().BeNull();
        live.IsStarted.Should().BeFalse();
        live.IsUnavailable.Should().BeTrue();
        live.Message.Should().Be(Strings.Wmi2_Unavailable);
    }

    [Fact]
    public async Task Nothing_is_switched_before_reading_has_started()
    {
        using var live = Create();
        var opens = 0;

        await live.SwitchSourceAsync(_ =>
        {
            opens++;
            return Task.FromResult<IWmi2LiveSource?>(_other);
        }).WaitAsync(Settle);

        opens.Should().Be(0);
        live.Source.Should().BeNull();
    }

    [Fact]
    public async Task A_source_that_opens_after_the_window_closed_is_closed_at_once()
    {
        var live = await StartedAsync();
        var opening = new TaskCompletionSource<IWmi2LiveSource?>();
        var switching = live.SwitchSourceAsync(_ => opening.Task);
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        SpinWait.SpinUntil(() => _readOnly.Disposed, Settle).Should().BeTrue();

        live.Dispose();
        opening.SetResult(_other);
        await switching.WaitAsync(Settle);

        _other.Disposed.Should().BeTrue();
        live.Source.Should().BeNull();
    }

    [Fact]
    public async Task The_firmware_and_the_status_of_the_last_round_are_kept_for_control()
    {
        _readOnly.Firmware = "15M3EMS1.105";
        using var live = await StartedAsync();

        live.FirmwareVersion.Should().Be("15M3EMS1.105");
        live.LastStatus.Should().Be(_readOnly.Status);
    }

    [Fact]
    public async Task Stop_forgets_the_firmware_and_the_status()
    {
        using var live = await StartedAsync();

        live.StopCommand.Execute(null);

        live.FirmwareVersion.Should().BeNull();
        live.LastStatus.Should().BeNull();
        live.Source.Should().BeNull();
    }

    [Fact]
    public async Task State_changes_are_announced_after_a_round_a_switch_and_stop()
    {
        var live = Create();
        live.SetWindowShown(true);
        var announced = 0;
        live.StateChanged += (_, _) => announced++;

        await live.ShowCommand.ExecuteAsync(null);
        announced.Should().Be(1);
        await SwitchAsync(live, _ => Task.FromResult<IWmi2LiveSource?>(_other));
        announced.Should().Be(2);
        live.StopCommand.Execute(null);
        announced.Should().Be(3);
        live.Dispose();
    }

    [Fact]
    public async Task A_round_is_announced_once_it_is_over_so_a_switch_can_follow_at_once()
    {
        using var live = Create();
        var busyWhenAnnounced = new List<bool>();
        live.StateChanged += (_, _) => busyWhenAnnounced.Add(live.IsBusy);

        await live.ShowCommand.ExecuteAsync(null);

        busyWhenAnnounced.Should().Equal(false);
    }

    [Fact]
    public async Task The_switch_says_whether_it_ran()
    {
        using var live = Create();
        (await live.SwitchSourceAsync(_ => Task.FromResult<IWmi2LiveSource?>(_other)).WaitAsync(Settle)).Should().BeFalse();
        await live.ShowCommand.ExecuteAsync(null);

        var switching = live.SwitchSourceAsync(_ => Task.FromResult<IWmi2LiveSource?>(null));
        _time.Advance(Wmi2ReadPolicy.MinInterval);

        (await switching.WaitAsync(Settle)).Should().BeTrue("it ran, even though the other source did not open");
    }

    [Fact]
    public async Task A_wanted_status_is_read_by_the_next_timed_round()
    {
        using var live = await StartedAsync();

        live.WantStatus();
        _time.Advance(Wmi2LiveViewModel.PollInterval);

        SpinWait.SpinUntil(() => _readOnly.StatusReads == 2, Settle).Should().BeTrue();
    }
}
