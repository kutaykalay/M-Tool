using Microsoft.Extensions.Time.Testing;
using MTool.App.ViewModels;
using MTool.Core.Device;
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
        _main = new MainViewModel(_poller, _service, _control, ProfileCatalog.BuiltIn, _notifier, new ImmediateDispatcher());
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
        _control.NextStatus = MTool.Core.Ec.WriteStatus.DryRun;
        await _main.InitializeAsync([]);

        await _main.Controls.SelectProfileCommand.ExecuteAsync("Silent");

        _main.TrayTooltip.Should().Contain("Default").And.NotContain("Silent");
    }

    [Fact]
    public void Intervals_match_the_plan()
    {
        MainViewModel.VisibleInterval.Should().Be(TimeSpan.FromSeconds(1));
        MainViewModel.HiddenInterval.Should().Be(TimeSpan.FromSeconds(5));
    }
}
