using MTool.App.ViewModels;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public sealed class PowerSwitchViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();
    private readonly FakePowerSource _source = new();
    private readonly StatusViewModel _status = new(new FakeNotifier());

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Starts_off_and_names_the_source_windows_reports()
    {
        var vm = ViewModel(Service());

        vm.IsOn.Should().BeFalse();
        vm.SourceLabel.Should().Be("Şu an: prizde.");
    }

    [Fact]
    public void Starts_on_when_the_settings_say_so()
    {
        var settings = AppSettings.Default with { PowerSwitch = new PowerSwitchSettings(Enabled: true) };

        ViewModel(Service(settings)).IsOn.Should().BeTrue();
    }

    [Theory]
    [InlineData(PowerSource.Battery, "Şu an: pilde.")]
    [InlineData(PowerSource.Ac, "Şu an: prizde.")]
    [InlineData(null, null)]
    public void The_source_label_follows_windows(PowerSource? source, string? expected)
    {
        _source.Current = source == PowerSource.Ac ? PowerSource.Battery : PowerSource.Ac;
        var vm = ViewModel(Service());

        _source.Set(source);

        vm.SourceLabel.Should().Be(expected);
    }

    [Fact]
    public void The_source_label_changes_on_the_ui_thread()
    {
        var ui = new QueuedDispatcher();
        var vm = ViewModel(Service(), ui);

        _source.Set(PowerSource.Battery);
        vm.SourceLabel.Should().Be("Şu an: prizde.");

        ui.RunAll();
        vm.SourceLabel.Should().Be("Şu an: pilde.");
    }

    [Fact]
    public async Task Toggling_turns_switching_on_and_off_without_touching_the_ec()
    {
        var service = Service();
        var vm = ViewModel(service);

        await vm.ToggleCommand.ExecuteAsync(null);
        vm.IsOn.Should().BeTrue();
        service.PowerSwitch.Enabled.Should().BeTrue();

        await vm.ToggleCommand.ExecuteAsync(null);
        vm.IsOn.Should().BeFalse();
        service.PowerSwitch.Enabled.Should().BeFalse();

        _control.Calls.Should().BeEmpty();
        _status.Message.Should().BeNull();
    }

    [Fact]
    public async Task Toggling_is_saved_to_the_settings_file()
    {
        var vm = ViewModel(Service());

        await vm.ToggleCommand.ExecuteAsync(null);

        new SettingsStore(_folder).Load().Settings.PowerSwitch.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Toggling_works_while_writing_is_locked()
    {
        _control.Access = _control.Access with { WriteMode = Core.Device.WriteMode.Locked };
        var vm = ViewModel(Service());

        vm.ToggleCommand.CanExecute(null).Should().BeTrue();
        await vm.ToggleCommand.ExecuteAsync(null);

        vm.IsOn.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_save_is_a_warning_and_the_switch_shows_what_runs_now()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "");
        var service = Service(folder: blocked);
        var vm = ViewModel(service);

        await vm.ToggleCommand.ExecuteAsync(null);

        vm.IsOn.Should().Be(service.PowerSwitch.Enabled);
        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Contain("settings.json");
    }

    private PowerSwitchViewModel ViewModel(ProfileService service, IUiDispatcher? ui = null) =>
        new(service, _source, _status, ui ?? new ImmediateDispatcher());

    private ProfileService Service(AppSettings? settings = null, string? folder = null) =>
        new(_control, ProfileCatalog.BuiltIn, new SettingsStore(folder ?? _folder), settings ?? AppSettings.Default, new ListLog());
}
