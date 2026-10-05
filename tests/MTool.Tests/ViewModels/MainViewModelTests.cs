using Microsoft.Extensions.Time.Testing;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Sensors;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeTimeProvider _time = new();
    private readonly FakeP65Control _control = new();
    private readonly FakeNotifier _notifier = new();
    private readonly ProfileService _service;
    private readonly SensorPoller _poller;
    private readonly MainViewModel _main;

    public MainViewModelTests()
    {
        var log = new ListLog();
        _service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default, log);
        _poller = new SensorPoller(_control.ReadSensorsAsync, () => true, () => _service.IsBusy, _time, log, MainViewModel.HiddenInterval);
        _main = new MainViewModel(_poller, _service, _control, _notifier, new ImmediateDispatcher());
    }

    public void Dispose()
    {
        _poller.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Initialize_shows_access_startup_warnings_and_the_ec_state()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.DryRun };

        await _main.InitializeAsync(["settings.json okunamadı"]);

        _main.Status.AccessBanner.Should().Contain("DRY-RUN");
        _main.Status.Message.Should().Contain("settings.json okunamadı");
        _main.Controls.ActiveProfile.Should().Be("Default");
    }

    [Fact]
    public async Task Showing_the_window_polls_every_second_and_rereads_the_ec()
    {
        await _main.InitializeAsync([]);
        _control.State = _control.State with { FanCurves = Presets.Cool.Curves };

        await _main.OnWindowShownAsync();

        _poller.Interval.Should().Be(MainViewModel.VisibleInterval);
        _main.Controls.ActiveProfile.Should().Be("Cool");
    }

    [Fact]
    public async Task Hiding_the_window_slows_polling_down()
    {
        await _main.OnWindowShownAsync();

        _main.OnWindowHidden();

        _poller.Interval.Should().Be(MainViewModel.HiddenInterval);
    }

    [Fact]
    public async Task Sensor_readings_reach_the_window_and_the_tooltip()
    {
        await _main.InitializeAsync([]);

        await _poller.PollNowAsync();

        _main.Sensors.CpuTemperature.Should().Be("60 °C");
        _main.TrayTooltip.Should().Contain("CPU 60°C").And.Contain("Default");
    }

    [Fact]
    public async Task The_tooltip_follows_the_profile_the_ec_holds()
    {
        await _main.InitializeAsync([]);

        await _main.Controls.SelectProfileCommand.ExecuteAsync("Silent");

        _main.TrayTooltip.Should().Contain("Silent");
    }

    [Fact]
    public async Task The_tooltip_does_not_claim_a_profile_the_ec_does_not_hold()
    {
        _control.Access = _control.Access with { WriteMode = WriteMode.DryRun };
        _control.NextStatus = WriteStatus.DryRun;
        await _main.InitializeAsync([]);

        await _main.Controls.SelectProfileCommand.ExecuteAsync("Silent");

        _main.TrayTooltip.Should().Contain("Default").And.NotContain("Silent");
    }

    // --- start-up and automatic reapplying ---

    [Fact]
    public async Task Initialize_does_not_read_the_port()
    {
        await _main.InitializeAsync([]);

        _control.StateReadPortUses.Should().Equal(PortUse.None);
    }

    [Fact]
    public async Task Showing_the_window_reads_the_port()
    {
        await _main.InitializeAsync([]);

        await _main.OnWindowShownAsync();

        _control.StateReadPortUses.Should().Equal(PortUse.None, PortUse.Allowed);
    }

    [Theory]
    [InlineData(WriteStatus.Applied)]
    [InlineData(WriteStatus.DryRun)]
    public async Task A_successful_automatic_reapply_refreshes_quietly_without_the_port(WriteStatus status)
    {
        await _main.InitializeAsync([]);
        _control.State = _control.State with { FanCurves = Presets.Cool.Curves };

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Resume, status));

        _main.Controls.ActiveProfile.Should().Be("Cool");
        _main.TrayTooltip.Should().Contain("Cool");
        _main.Status.Message.Should().BeNull();
        _notifier.Errors.Should().BeEmpty();
        _control.StateReadPortUses.Should().OnlyContain(p => p == PortUse.None);
    }

    [Fact]
    public async Task A_rejected_automatic_reapply_shows_a_warning_without_a_balloon()
    {
        await _main.InitializeAsync([]);

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Startup, WriteStatus.Applied, WriteStatus.Rejected));

        _main.Status.MessageKind.Should().Be(MessageKind.Warning);
        _main.Status.Message.Should().Contain("Otomatik yeniden uygulama").And.Contain(nameof(WriteStatus.Rejected));
        _notifier.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WriteStatus.FailedRecovered)]
    [InlineData(WriteStatus.FailedUnrecovered)]
    public async Task A_failed_automatic_reapply_shows_an_error_and_a_balloon(WriteStatus status)
    {
        await _main.InitializeAsync([]);

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Resume, status));

        _main.Status.MessageKind.Should().Be(MessageKind.Error);
        _main.Status.Message.Should().Contain("Otomatik yeniden uygulama");
        _notifier.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task An_automatic_reapply_with_nothing_to_do_shows_nothing()
    {
        await _main.InitializeAsync([]);

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Startup));

        _main.Status.Message.Should().BeNull();
        _notifier.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_later_success_clears_an_earlier_automatic_warning()
    {
        await _main.InitializeAsync([]);
        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Startup, WriteStatus.Rejected));

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Retry, WriteStatus.Applied));

        _main.Status.Message.Should().BeNull();
    }

    [Fact]
    public async Task A_successful_automatic_reapply_keeps_other_messages()
    {
        await _main.InitializeAsync(["settings.json okunamadı"]);

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Startup, WriteStatus.Applied));

        _main.Status.Message.Should().Contain("settings.json okunamadı");
    }

    [Fact]
    public async Task A_later_success_clears_an_earlier_automatic_error()
    {
        await _main.InitializeAsync([]);
        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Resume, WriteStatus.FailedRecovered));

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Resume, WriteStatus.Applied));

        _main.Status.Message.Should().BeNull();
    }

    [Fact]
    public async Task A_success_keeps_a_newer_message_that_replaced_the_automatic_one()
    {
        await _main.InitializeAsync([]);
        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Startup, WriteStatus.Rejected));
        _main.Status.ShowWarning("başka bir uyarı");

        await _main.OnAutoReappliedAsync(Result(ReapplyTrigger.Retry, WriteStatus.Applied));

        _main.Status.Message.Should().Be("başka bir uyarı");
    }

    private static AutoReapplyResult Result(ReapplyTrigger trigger, params WriteStatus[] statuses) =>
        new(trigger, [.. statuses.Select(s => new WriteOutcome(s, [], s.ToString()))]);

    [Fact]
    public void Intervals_match_the_plan()
    {
        MainViewModel.VisibleInterval.Should().Be(TimeSpan.FromSeconds(1));
        MainViewModel.HiddenInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_tooltip_follows_a_renamed_custom_profile()
    {
        await _service.AddProfileAsync("Gece", TestCurves.Night);
        await _main.Controls.SelectProfileCommand.ExecuteAsync("Gece");
        _main.TrayTooltip.Should().Contain("Gece");

        await _service.RenameProfileAsync("Gece", "Uyku");

        _main.TrayTooltip.Should().Contain("Uyku").And.NotContain("Gece");
    }
}
