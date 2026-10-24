using Microsoft.Extensions.Time.Testing;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Sensors;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public class Wmi2LiveViewModelTests
{
    private readonly FakeWmi2LiveSource _source = new();
    private readonly FakeConfirm _confirm = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ListLog _log = new();
    private readonly Wmi2CircuitBreaker _breaker = new();
    private bool _gateOpen = true;
    private int _opened;

    private Wmi2LiveViewModel Create(Func<IWmi2LiveSource?>? open = null) =>
        new(open ?? (() =>
        {
            _opened++;
            return _source;
        }), _breaker, _confirm, () => _gateOpen, _time, _log, WmiInterface.Wmi2, list: null);

    private static string Unverified(string name, string raw) => string.Format(Strings.Wmi2_Unverified, name, raw);

    [Fact]
    public async Task Show_asks_first_with_no_as_the_default_and_no_reads_nothing()
    {
        _confirm.Answer = false;
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        _confirm.Questions.Should().Equal(Strings.Wmi2_Confirm);
        _confirm.DefaultsToYes.Should().Equal(false);
        _opened.Should().Be(0);
        _source.Reads.Should().Be(0);
        live.IsStarted.Should().BeFalse();
        _log.Lines.Should().Contain(line => line.Contains("not confirmed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Yes_opens_once_and_reads_the_firmware_the_status_and_the_sensors_once()
    {
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        _opened.Should().Be(1);
        (_source.FirmwareReads, _source.StatusReads, _source.SensorReads).Should().Be((1, 1, 1));
        live.IsStarted.Should().BeTrue();
        live.Firmware.Should().Be("16Q4EMS2.107");
        live.CpuTemperature.Should().Be("60 °C");
        live.GpuTemperature.Should().Be("46 °C");
        live.Message.Should().BeNull();
        _log.Lines.Should().Contain(line => line.Contains("WMI2 live read: started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Show_is_offered_only_until_it_started()
    {
        var live = Create();
        live.ShowCommand.CanExecute(null).Should().BeTrue();
        live.RefreshCommand.CanExecute(null).Should().BeFalse();

        await live.ShowCommand.ExecuteAsync(null);

        live.ShowCommand.CanExecute(null).Should().BeFalse();
        live.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_within_two_seconds_reads_nothing_and_later_reads_the_status_and_sensors_without_asking()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);

        _time.Advance(Wmi2ReadPolicy.MinInterval - TimeSpan.FromMilliseconds(1));
        await live.RefreshCommand.ExecuteAsync(null);
        _source.Reads.Should().Be(3);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await live.RefreshCommand.ExecuteAsync(null);

        (_source.FirmwareReads, _source.StatusReads, _source.SensorReads).Should().Be((1, 2, 2));
        _confirm.Questions.Should().HaveCount(1);
        _opened.Should().Be(1);
    }

    [Fact]
    public async Task While_the_gate_is_closed_nothing_is_opened_or_read_and_it_says_paused()
    {
        _gateOpen = false;
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        _opened.Should().Be(0);
        _source.Reads.Should().Be(0);
        live.Message.Should().Be(Strings.Sensors_Paused);
        live.MessageKind.Should().Be(MessageKind.Info);
    }

    [Fact]
    public async Task A_closed_gate_on_refresh_reads_nothing_and_does_not_count_as_a_failure()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        _gateOpen = false;

        for (var i = 0; i < Wmi2CircuitBreaker.MaxFailuresInARow + 1; i++)
        {
            _time.Advance(Wmi2ReadPolicy.MinInterval);
            await live.RefreshCommand.ExecuteAsync(null);
        }

        _source.Reads.Should().Be(3);
        live.Message.Should().Be(Strings.Sensors_Paused);
        _breaker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task One_failed_read_keeps_the_old_values_and_refresh_stays_on()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        _source.Error = FakeWmi2LiveSource.Timeout();
        _source.Sensors = _source.Sensors with { CpuTempC = 90 };

        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await live.RefreshCommand.ExecuteAsync(null);

        live.Message.Should().Be(Strings.Wmi2_ReadFailed);
        live.MessageKind.Should().Be(MessageKind.Warning);
        live.IsStopped.Should().BeFalse();
        live.CpuTemperature.Should().Be("60 °C");
        live.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Two_failed_reads_in_a_row_stop_reading_until_try_again()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        _source.Error = FakeWmi2LiveSource.Timeout();

        await FailedRefreshesAsync(live, Wmi2CircuitBreaker.MaxFailuresInARow);
        var reads = _source.Reads;
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await live.RefreshCommand.ExecuteAsync(null);

        live.Message.Should().Be(Strings.Wmi2_Stopped);
        live.MessageKind.Should().Be(MessageKind.Warning);
        live.IsStopped.Should().BeTrue();
        live.RefreshCommand.CanExecute(null).Should().BeFalse();
        live.TryAgainCommand.CanExecute(null).Should().BeTrue();
        _source.Reads.Should().Be(reads);
        _log.Lines.Count(line => line.Contains("breaker open", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task Try_again_resets_the_breaker_once_and_reads_again_without_asking()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        _source.Error = FakeWmi2LiveSource.Timeout();
        await FailedRefreshesAsync(live, Wmi2CircuitBreaker.MaxFailuresInARow);
        _source.Error = null;
        var statusReads = _source.StatusReads;
        _time.Advance(Wmi2ReadPolicy.MinInterval);

        await live.TryAgainCommand.ExecuteAsync(null);

        _breaker.IsOpen.Should().BeFalse();
        live.IsStopped.Should().BeFalse();
        live.Message.Should().BeNull();
        _source.StatusReads.Should().Be(statusReads + 1);
        _confirm.Questions.Should().HaveCount(1);
        live.TryAgainCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task The_stopped_state_comes_from_the_breaker_itself()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        for (var i = 0; i < Wmi2CircuitBreaker.MaxFailuresInARow; i++)
        {
            // Another reader on the same breaker (the periodic poller) failed.
            await FluentActions.Awaiting(() => _breaker.RunAsync<int>(_ => throw FakeWmi2LiveSource.Timeout(), CancellationToken.None))
                .Should().ThrowAsync<EcAccessException>();
        }

        var reads = _source.Reads;
        _time.Advance(Wmi2ReadPolicy.MinInterval);
        await live.RefreshCommand.ExecuteAsync(null);

        _source.Reads.Should().Be(reads);
        live.IsStopped.Should().BeTrue();
        live.Message.Should().Be(Strings.Wmi2_Stopped);
    }

    [Fact]
    public async Task An_interface_that_cannot_be_opened_reads_nothing_and_is_not_offered_again()
    {
        var live = Create(() => throw new EcAccessException("MSI_ACPI connection did not answer within 2 s"));

        await live.ShowCommand.ExecuteAsync(null);

        live.Message.Should().Be(Strings.Wmi2_Unavailable);
        live.MessageKind.Should().Be(MessageKind.Warning);
        live.IsStarted.Should().BeFalse();
        live.ShowCommand.CanExecute(null).Should().BeFalse();
        _log.Lines.Should().Contain(line => line.StartsWith("WARN WMI2 live read", StringComparison.Ordinal) && line.Contains("did not answer"));
    }

    [Fact]
    public async Task No_wmi2_after_all_reads_nothing()
    {
        var live = Create(() => null);

        await live.ShowCommand.ExecuteAsync(null);

        live.Message.Should().Be(Strings.Wmi2_Unavailable);
        live.ShowCommand.CanExecute(null).Should().BeFalse();
        _source.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Messages_never_carry_the_error_text()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        _source.Error = new EcAccessException(@"C:\Users\kutay\AppData failed");

        await FailedRefreshesAsync(live, 1);
        live.Message.Should().NotContain("kutay").And.NotContain(@"C:\");

        await FailedRefreshesAsync(live, 1);
        live.Message.Should().NotContain("kutay").And.NotContain(@"C:\");
    }

    [Fact]
    public async Task Modes_show_the_suggested_name_with_the_raw_byte_and_not_verified()
    {
        _source.Status = new Wmi2Status(CoolerBoostRaw: 0x80, PerformanceRaw: 0xC1, FanModeRaw: 0x8D, ChargeRaw: 0xD0);
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        live.PerformanceMode.Should().Be(Unverified(Strings.Mode_Balanced, "0xC1"));
        live.FanMode.Should().Be(Unverified(Strings.Wmi2_FanAdvanced, "0x8D"));
        live.ChargeLimit.Should().Be(Unverified("80%", "0xD0"));
        live.CoolerBoost.Should().Be(Unverified(Strings.Wmi2_On, "0x80"));
    }

    [Theory]
    [InlineData(0xC2, nameof(Strings.Mode_Eco))]
    [InlineData(0xC4, nameof(Strings.Mode_High))]
    public async Task Each_known_performance_mode_has_its_name(byte raw, string key)
    {
        _source.Status = _source.Status with { PerformanceRaw = raw };
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        live.PerformanceMode.Should().Be(Unverified(Strings.ResourceManager.GetString(key)!, $"0x{raw:X2}"));
    }

    [Fact]
    public async Task Unknown_values_show_only_the_raw_byte()
    {
        _source.Status = new Wmi2Status(CoolerBoostRaw: 0x00, PerformanceRaw: 0xC0, FanModeRaw: 0x4C, ChargeRaw: 0x50);
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        live.PerformanceMode.Should().Be("0xC0");
        live.FanMode.Should().Be("0x4C");
        live.ChargeLimit.Should().Be("0x50");
        live.CoolerBoost.Should().Be(Unverified(Strings.Wmi2_Off, "0x00"));
    }

    [Theory]
    [InlineData(0x0D, nameof(Strings.Wmi2_FanAuto))]
    [InlineData(0x1D, nameof(Strings.Wmi2_FanSilent))]
    public async Task Each_known_fan_mode_has_its_name(byte raw, string key)
    {
        _source.Status = _source.Status with { FanModeRaw = raw };
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        live.FanMode.Should().Be(Unverified(Strings.ResourceManager.GetString(key)!, $"0x{raw:X2}"));
    }

    [Fact]
    public async Task Fans_show_the_percent_and_the_speed_or_the_raw_bytes_when_the_speed_is_unclear()
    {
        _source.Sensors = new SensorSnapshot(60, null, 45, 30, 2987, 0, new Wmi2Tach(0, 0xA0, 0x01, 0xFF));
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        live.CpuFan.Should().Be("45% · 2987 rpm");
        live.GpuFan.Should().Be($"30% · {string.Format(Strings.Wmi2_Raw, "0x01 0xFF")}");
        live.GpuTemperature.Should().Be("—");
    }

    [Fact]
    public void Before_any_read_every_value_is_a_dash()
    {
        var live = Create();

        new[] { live.Firmware, live.CpuTemperature, live.GpuTemperature, live.CpuFan, live.GpuFan, live.PerformanceMode, live.FanMode, live.ChargeLimit, live.CoolerBoost }
            .Should().AllBe("—");
    }

    [Fact]
    public async Task An_unreadable_firmware_is_a_dash()
    {
        _source.Firmware = null;
        var live = Create();

        await live.ShowCommand.ExecuteAsync(null);

        live.Firmware.Should().Be("—");
    }

    [Fact]
    public async Task Dispose_closes_the_source()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);

        live.Dispose();

        _source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task An_interface_that_fails_in_any_other_way_is_unavailable_too()
    {
        var live = Create(() => throw new UnauthorizedAccessException("Access_EC"));

        await live.ShowCommand.ExecuteAsync(null);

        live.Message.Should().Be(Strings.Wmi2_Unavailable);
        live.ShowCommand.CanExecute(null).Should().BeFalse();
        _log.Lines.Should().Contain(line => line.StartsWith("WARN WMI2 live read", StringComparison.Ordinal) && line.Contains("Access_EC"));
    }

    [Fact]
    public async Task Try_again_within_two_seconds_of_the_last_round_reads_nothing()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        _source.Error = FakeWmi2LiveSource.Timeout();
        await FailedRefreshesAsync(live, Wmi2CircuitBreaker.MaxFailuresInARow);
        _source.Error = null;
        var reads = _source.Reads;

        await live.TryAgainCommand.ExecuteAsync(null);

        _source.Reads.Should().Be(reads);
        _breaker.IsOpen.Should().BeTrue("nothing was tried, so the breaker stays as it was");
        live.IsStopped.Should().BeTrue();
    }

    [Fact]
    public async Task Sleep_during_a_round_says_paused_and_is_not_a_failure()
    {
        var live = Create();
        await live.ShowCommand.ExecuteAsync(null);
        _source.Error = new EcAccessException("paused") { IsAccessPaused = true };

        await FailedRefreshesAsync(live, Wmi2CircuitBreaker.MaxFailuresInARow);

        live.Message.Should().Be(Strings.Sensors_Paused);
        live.IsStopped.Should().BeFalse();
        _log.Lines.Should().NotContain(line => line.StartsWith("WARN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Closing_during_a_round_stops_it_without_counting_a_failure()
    {
        var live = Create();
        _source.HoldStatus = new TaskCompletionSource();
        var show = live.ShowCommand.ExecuteAsync(null);
        SpinWait.SpinUntil(() => _source.StatusReads == 1, TimeSpan.FromSeconds(5)).Should().BeTrue("the round must be running");

        live.Dispose();
        _source.HoldStatus.SetResult();
        await show;

        _source.SensorReads.Should().Be(0);
        _source.Disposed.Should().BeTrue();
        _log.Lines.Should().NotContain(line => line.StartsWith("WARN", StringComparison.Ordinal));
        await FluentActions.Awaiting(() => _breaker.RunAsync<int>(_ => throw FakeWmi2LiveSource.Timeout(), CancellationToken.None))
            .Should().ThrowAsync<EcAccessException>();
        _breaker.IsOpen.Should().BeFalse("the round cut short by closing did not count");
    }

    [Fact]
    public async Task A_source_that_opens_after_closing_is_closed_at_once_and_never_read()
    {
        using var opening = new ManualResetEventSlim();
        var live = Create(() =>
        {
            opening.Wait();
            return _source;
        });
        var show = live.ShowCommand.ExecuteAsync(null);

        live.Dispose();
        opening.Set();
        await show;

        _source.Disposed.Should().BeTrue();
        _source.Reads.Should().Be(0);
        live.IsStarted.Should().BeFalse();
    }

    private async Task FailedRefreshesAsync(Wmi2LiveViewModel live, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _time.Advance(Wmi2ReadPolicy.MinInterval);
            await live.RefreshCommand.ExecuteAsync(null);
        }
    }
}
