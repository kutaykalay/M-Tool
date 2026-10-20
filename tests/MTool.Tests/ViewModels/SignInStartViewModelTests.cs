using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Tests.Fakes;

namespace MTool.Tests.ViewModels;

public class SignInStartViewModelTests
{
    private const string InstalledExe = @"C:\Program Files\M-Tool\M-Tool.exe";
    private const string DownloadedExe = @"C:\Users\PC\Downloads\M-Tool.exe";

    private readonly FakeStartupTask _task = new();
    private readonly StatusViewModel _status = new(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System);
    private readonly ListLog _log = new();
    private readonly HashSet<string> _missingExes = [];

    private SignInStartViewModel ViewModel(string exe = InstalledExe) =>
        new(_task, exe, _status, _log, path => !_missingExes.Contains(path));

    private async Task<SignInStartViewModel> Loaded(string exe = InstalledExe)
    {
        var vm = ViewModel(exe);
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public void Before_loading_the_state_is_unknown()
    {
        var vm = ViewModel();

        vm.IsKnown.Should().BeFalse();
        vm.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Load_shows_an_existing_task_as_enabled()
    {
        _task.RegisteredExe = InstalledExe;

        var vm = await Loaded();

        vm.IsKnown.Should().BeTrue();
        vm.IsEnabled.Should().BeTrue();
        _status.Message.Should().BeNull();
    }

    [Fact]
    public async Task Load_without_a_task_shows_it_as_disabled()
    {
        var vm = await Loaded();

        vm.IsKnown.Should().BeTrue();
        vm.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_task_for_another_exe_is_reported_not_repaired()
    {
        _task.RegisteredExe = DownloadedExe;

        var vm = await Loaded();

        vm.IsEnabled.Should().BeTrue();
        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Contain(DownloadedExe);
        _log.Lines.Should().Contain(l => l.StartsWith("WARN") && l.Contains(DownloadedExe));
        _task.Calls.Should().Equal("query");
    }

    [Fact]
    public async Task A_task_for_an_exe_that_no_longer_exists_says_so()
    {
        _task.RegisteredExe = DownloadedExe;
        _missingExes.Add(DownloadedExe);

        await Loaded();

        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Contain("artık var olmayan bir exe").And.Contain(DownloadedExe);
        _log.Lines.Should().ContainSingle().Which.Should().Contain($"an exe that no longer exists: {DownloadedExe}");
        _task.Calls.Should().Equal("query");
    }

    [Fact]
    public async Task A_task_for_this_exe_is_not_checked_for_a_missing_file()
    {
        _task.RegisteredExe = InstalledExe;
        _missingExes.Add(InstalledExe);

        await Loaded();

        _status.Message.Should().BeNull();
    }

    [Fact]
    public async Task A_task_by_this_name_that_is_not_ours_is_reported()
    {
        _task.RegisteredExe = "";

        await Loaded();

        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Contain("tanımadığı");
    }

    [Fact]
    public async Task A_garbage_registered_path_is_reported_without_crashing()
    {
        _task.RegisteredExe = "C:\bad\0path.exe";

        var load = async () => await Loaded();

        await load.Should().NotThrowAsync();
        _status.MessageKind.Should().Be(MessageKind.Warning);
    }

    [Fact]
    public async Task The_load_warning_is_added_to_an_earlier_message_not_put_in_its_place()
    {
        _status.ShowWarning("settings.json okunamadı.");
        _task.RegisteredExe = DownloadedExe;

        await Loaded();

        _status.Message.Should().Contain("settings.json okunamadı.").And.Contain(DownloadedExe);
    }

    [Fact]
    public async Task A_failed_query_leaves_the_state_unknown()
    {
        _task.RegisteredExe = InstalledExe;
        _task.Failure = new UnauthorizedAccessException("denied");

        var vm = await Loaded();

        vm.IsKnown.Should().BeFalse();
        _log.Lines.Should().Contain(l => l.Contains("denied"));
    }

    [Fact]
    public async Task Toggle_on_registers_this_exe()
    {
        var vm = await Loaded();

        await vm.ToggleCommand.ExecuteAsync(null);

        _task.Calls.Should().Contain($"enable {InstalledExe}");
        vm.IsEnabled.Should().BeTrue();
        _status.Message.Should().BeNull();
    }

    [Fact]
    public async Task Toggle_off_removes_the_task()
    {
        _task.RegisteredExe = InstalledExe;
        var vm = await Loaded();

        await vm.ToggleCommand.ExecuteAsync(null);

        _task.Calls.Should().Contain("disable");
        vm.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Toggle_is_not_possible_while_the_state_is_unknown()
    {
        var vm = ViewModel();

        vm.ToggleCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Toggle_on_registers_the_exe_wherever_it_is_without_a_warning()
    {
        var vm = await Loaded(DownloadedExe);

        await vm.ToggleCommand.ExecuteAsync(null);

        _task.Calls.Should().Contain($"enable {DownloadedExe}");
        vm.IsEnabled.Should().BeTrue();
        _status.Message.Should().BeNull();
    }

    [Fact]
    public async Task Toggling_off_and_on_repairs_a_task_for_another_exe_and_clears_the_warning()
    {
        _task.RegisteredExe = DownloadedExe;
        var vm = await Loaded();

        await vm.ToggleCommand.ExecuteAsync(null);
        await vm.ToggleCommand.ExecuteAsync(null);

        _task.RegisteredExe.Should().Be(InstalledExe);
        _status.Message.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_toggle_warns_and_shows_the_real_state()
    {
        _task.RegisteredExe = InstalledExe;
        var vm = await Loaded();
        _task.DisableFailure = new UnauthorizedAccessException("denied");

        await vm.ToggleCommand.ExecuteAsync(null);

        vm.IsKnown.Should().BeTrue();
        vm.IsEnabled.Should().BeTrue();
        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Be(Strings.SignIn_ChangeFailed);
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("denied"));
    }

    [Fact]
    public async Task A_failed_toggle_keeps_the_reason_and_its_paths_out_of_the_band()
    {
        var vm = await Loaded();
        _task.Failure = new UnauthorizedAccessException($@"Access to {DownloadedExe} is denied.");

        await vm.ToggleCommand.ExecuteAsync(null);

        _status.Message.Should().NotContain(DownloadedExe).And.NotContain("denied");
        _log.Lines.Should().Contain(l => l.Contains(DownloadedExe), "the log keeps the reason");
    }

    [Fact]
    public async Task A_failed_toggle_still_tells_the_check_box_the_state_it_did_not_change()
    {
        var vm = await Loaded();
        _task.Failure = new UnauthorizedAccessException("denied");
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ToggleCommand.ExecuteAsync(null);

        vm.IsEnabled.Should().BeFalse();
        changed.Should().Contain(nameof(SignInStartViewModel.IsEnabled), "the clicked check box shows on until it hears the state again");
    }
}
