using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Time.Testing;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

public sealed class ReportViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mtool-report-vm-").FullName;
    private readonly FakeNotifier _notifier = new();
    private readonly FakeConfirm _confirm = new();
    private readonly FakeShellLauncher _shell = new();
    private readonly ListLog _log = new();
    private readonly StatusViewModel _status;
    private readonly FakeTimeProvider _time = new();

    public ReportViewModelTests() => _status = new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System);

    private string Reports => Path.Combine(_root, "reports");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ReportViewModel ViewModel(FakeReportSources sources, string? folder = null, params string[] privateWords) =>
        new(new ReportService(sources, _log, folder ?? Reports, "0.9.0", privateWords), _confirm, _shell, _status, _notifier, _log, _time);

    private static Task Run(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        return ((IAsyncRelayCommand)command).ExecutionTask ?? Task.CompletedTask;
    }

    [Fact]
    public async Task Creating_writes_the_zip_and_tells_where_in_a_balloon_and_the_band()
    {
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.CreateCommand);

        var path = vm.LastReportPath!;
        File.Exists(path).Should().BeTrue();
        Path.GetDirectoryName(path).Should().Be(Reports);
        var shown = @"%AppData%\M-Tool\reports\" + Path.GetFileName(path);
        _notifier.Infos.Select(i => i.Message).Should().Contain(m => m.Contains(shown, StringComparison.Ordinal) && m.Contains("zip"));
        _notifier.Errors.Should().BeEmpty();
        _status.MessageKind.Should().Be(MessageKind.Info);
        _status.Message.Should().Contain(shown).And.Contain("report.txt");
    }

    [Fact]
    public async Task The_balloon_and_the_band_never_show_the_full_path_or_the_user_name()
    {
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.CreateCommand);

        var path = vm.LastReportPath!;
        var shown = _notifier.Infos.Select(i => i.Message).Append(_status.Message!).ToList();
        shown.Should().NotBeEmpty();
        foreach (var text in shown)
        {
            text.Should().NotContain(Reports).And.NotContain(_root).And.NotContain(Path.GetTempPath())
                .And.NotContain(Environment.UserName).And.NotContain(path);
        }

        _log.Lines.Should().Contain(l => l.Contains(path, StringComparison.Ordinal), "the full path is for the log");
    }

    [Fact]
    public async Task The_saved_message_ends_the_name_before_the_how_to_send_sentence()
    {
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.CreateCommand);

        _notifier.Infos.Select(i => i.Message).Should().Contain(m => m.Contains(".zip. ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_wmi2_question_is_asked_with_no_as_the_default_answer()
    {
        var vm = ViewModel(new FakeReportSources { Wmi = WmiInterface.Wmi2 });
        await vm.LoadAsync();

        await Run(vm.CreateWithWmi2Command);

        _confirm.DefaultsToYes.Should().Equal(false);
    }

    [Fact]
    public async Task A_report_that_hangs_gives_up_after_the_timeout_and_frees_the_commands()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);

        var running = Run(vm.CreateCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        vm.IsBusy.Should().BeTrue();
        await AdvanceUntilDone(running, ReportViewModel.ReportTimeout);

        vm.IsBusy.Should().BeFalse();
        vm.CreateCommand.CanExecute(null).Should().BeTrue();
        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Be(Strings.Report_TimedOut);
        _notifier.Errors.Should().ContainSingle().Which.Message.Should().Be(Strings.Report_TimedOut);
        vm.LastReportPath.Should().BeNull();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR", StringComparison.Ordinal));
        gate.Set();
        await WaitForTheAbandonedWork();
    }

    [Fact]
    public async Task The_timeout_is_a_few_minutes_not_seconds()
    {
        ReportViewModel.ReportTimeout.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(2)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_report_that_is_only_slow_is_not_cut_short()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);

        var running = Run(vm.CreateCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        _time.Advance(ReportViewModel.ReportTimeout - TimeSpan.FromSeconds(1));
        gate.Set();
        await running;

        vm.LastReportPath.Should().NotBeNull();
        _notifier.Errors.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task The_work_left_behind_by_a_timeout_does_not_report_later()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);
        var running = Run(vm.CreateCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await AdvanceUntilDone(running, ReportViewModel.ReportTimeout);
        var infosAfterTimeout = _notifier.Infos.Count;

        gate.Set();
        await WaitForTheAbandonedWork();

        vm.LastReportPath.Should().BeNull("the late result is ignored");
        _notifier.Infos.Count.Should().Be(infosAfterTimeout);
        _status.MessageKind.Should().Be(MessageKind.Warning);
        vm.IsBusy.Should().BeFalse();
    }

    /// <summary>The abandoned work still writes its zip; wait for it so the test folder can be deleted.</summary>
    private async Task WaitForTheAbandonedWork()
    {
        for (var i = 0; i < 200 && !(Directory.Exists(Reports) && Directory.GetFiles(Reports, "*.zip").Length > 0); i++)
        {
            await Task.Delay(25);
        }
    }

    /// <summary>The timer is set up a moment after the work starts, so advance until the command is done.</summary>
    private async Task AdvanceUntilDone(Task running, TimeSpan by)
    {
        for (var i = 0; i < 200 && !running.IsCompleted; i++)
        {
            _time.Advance(by);
            await Task.Delay(25);
        }

        running.IsCompleted.Should().BeTrue("the timeout must end the command");
        await running;
    }

    [Fact]
    public async Task Creating_does_not_open_the_browser_or_ask_anything()
    {
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.CreateCommand);

        _shell.Urls.Should().BeEmpty();
        _shell.Folders.Should().BeEmpty();
        _confirm.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task The_plain_report_never_calls_wmi2_even_on_a_wmi2_laptop()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        await Run(vm.CreateCommand);

        sources.Wmi2Reads.Should().Be(0);
        _confirm.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task While_a_report_runs_neither_report_command_can_execute()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2, Gate = gate };
        var vm = ViewModel(sources);
        await vm.LoadAsync();
        vm.CreateCommand.CanExecute(null).Should().BeTrue();

        var running = Run(vm.CreateCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        vm.CreateCommand.CanExecute(null).Should().BeFalse();
        vm.CreateWithWmi2Command.CanExecute(null).Should().BeFalse();
        vm.IsBusy.Should().BeTrue();
        gate.Set();
        await running;
        vm.CreateCommand.CanExecute(null).Should().BeTrue();
        vm.CreateWithWmi2Command.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_second_start_while_one_runs_does_not_make_a_second_report()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);

        var running = Run(vm.CreateCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        vm.CreateCommand.Execute(null);
        gate.Set();
        await running;

        sources.InterfaceReads.Should().Be(1);
        Directory.GetFiles(Reports).Should().ContainSingle();
    }

    [Fact]
    public async Task The_blocking_work_does_not_run_on_the_calling_thread()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);

        var running = Run(vm.CreateCommand); // returns while ReadSystem still waits on the gate

        running.IsCompleted.Should().BeFalse();
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        gate.Set();
        await running;
    }

    [Fact]
    public async Task A_failed_save_is_a_warning_and_a_balloon_without_path_or_user_name()
    {
        File.WriteAllText(Reports, "in the way"); // the reports folder cannot be made
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.CreateCommand);

        _status.MessageKind.Should().Be(MessageKind.Warning);
        var error = _notifier.Errors.Should().ContainSingle().Subject;
        error.Message.Should().Be(_status.Message);
        foreach (var text in new[] { _status.Message!, error.Message, error.Title })
        {
            text.Should().NotContain(_root).And.NotContain(Path.GetTempPath()).And.NotContain(Environment.UserName)
                .And.NotContain(@"\");
        }

        vm.LastReportPath.Should().BeNull();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR", StringComparison.Ordinal));
    }

    [Fact]
    public async Task After_a_failure_the_next_report_can_start()
    {
        File.WriteAllText(Reports, "in the way");
        var vm = ViewModel(new FakeReportSources());
        await Run(vm.CreateCommand);
        vm.CreateCommand.CanExecute(null).Should().BeTrue();

        File.Delete(Reports);
        await Run(vm.CreateCommand);

        vm.LastReportPath.Should().NotBeNull();
    }

    [Fact]
    public async Task The_wmi2_item_is_known_only_after_loading_and_only_on_a_wmi2_laptop()
    {
        var wmi1 = ViewModel(new FakeReportSources());
        var wmi2 = ViewModel(new FakeReportSources { Wmi = WmiInterface.Wmi2 });
        wmi2.HasWmi2.Should().BeFalse();
        wmi2.CreateWithWmi2Command.CanExecute(null).Should().BeFalse();

        await wmi1.LoadAsync();
        await wmi2.LoadAsync();

        wmi1.HasWmi2.Should().BeFalse();
        wmi2.HasWmi2.Should().BeTrue();
        wmi1.CreateWithWmi2Command.CanExecute(null).Should().BeFalse();
        wmi2.CreateWithWmi2Command.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task The_wmi2_report_without_the_answer_yes_never_calls_wmi2()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();
        _confirm.Answer = false;

        await Run(vm.CreateWithWmi2Command);

        sources.Wmi2Reads.Should().Be(0);
        sources.InterfaceReads.Should().Be(1, "only the detection at start-up, nothing from the report itself");
        _confirm.Questions.Should().ContainSingle();
        vm.LastReportPath.Should().BeNull();
        Directory.Exists(Reports).Should().BeFalse();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task The_wmi2_report_with_the_answer_yes_reads_wmi2_exactly_once()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        await Run(vm.CreateWithWmi2Command);

        sources.Wmi2Reads.Should().Be(1);
        _confirm.Questions.Should().ContainSingle();
        File.ReadAllText(Directory.GetFiles(Reports).Single()).Should().NotBeEmpty();
        vm.LastReportPath.Should().NotBeNull();
    }

    [Fact]
    public async Task The_question_says_what_will_happen()
    {
        var vm = ViewModel(new FakeReportSources { Wmi = WmiInterface.Wmi2 });
        await vm.LoadAsync();

        await Run(vm.CreateWithWmi2Command);

        _confirm.Questions.Single().Should().Contain("Get_").And.Contain("WMI2").And.Contain("report.txt").And.Contain("hex");
    }

    [Fact]
    public async Task The_wmi2_command_on_a_laptop_without_wmi2_does_nothing_even_if_forced()
    {
        var sources = new FakeReportSources();
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        vm.CreateWithWmi2Command.Execute(null);

        _confirm.Questions.Should().BeEmpty();
        sources.Wmi2Reads.Should().Be(0);
        sources.InterfaceReads.Should().Be(1);
    }

    [Fact]
    public async Task The_folder_command_opens_the_folder_of_the_last_report()
    {
        var vm = ViewModel(new FakeReportSources());
        await Run(vm.CreateCommand);

        vm.OpenFolderCommand.Execute(null);

        _shell.Folders.Should().Equal(Reports);
    }

    [Fact]
    public void The_folder_command_without_a_report_opens_nothing()
    {
        var vm = ViewModel(new FakeReportSources());

        vm.OpenFolderCommand.Execute(null);

        _shell.Folders.Should().BeEmpty();
        vm.LastReportPath.Should().BeNull();
    }

    [Fact]
    public async Task The_issue_command_opens_the_form_with_model_and_firmware_of_the_last_report()
    {
        var vm = ViewModel(new FakeReportSources());
        await Run(vm.CreateCommand);

        vm.ReportIssueCommand.Execute(null);

        _shell.Urls.Should().ContainSingle().Which.AbsoluteUri.Should().Be(IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107").AbsoluteUri);
    }

    [Fact]
    public async Task The_issue_command_without_a_report_makes_the_plain_report_first_and_opens_the_form_with_it()
    {
        var sources = new FakeReportSources();
        var vm = ViewModel(sources);

        await Run(vm.ReportIssueCommand);

        sources.InterfaceReads.Should().Be(1, "exactly one report is made");
        sources.Wmi2Reads.Should().Be(0);
        vm.LastReportPath.Should().NotBeNull();
        _shell.Urls.Should().ContainSingle().Which.AbsoluteUri.Should().Be(IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107").AbsoluteUri);
        _notifier.Infos.Select(i => i.Message).Should().Contain(m => m.Contains(".zip", StringComparison.Ordinal));
        _status.Message.Should().Contain("report.txt");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task The_issue_command_with_a_report_does_not_make_another()
    {
        var sources = new FakeReportSources();
        var vm = ViewModel(sources);
        await Run(vm.CreateCommand);

        await Run(vm.ReportIssueCommand);
        await Run(vm.ReportIssueCommand);

        sources.InterfaceReads.Should().Be(1);
        Directory.GetFiles(Reports).Should().ContainSingle();
        _shell.Urls.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_issue_command_does_not_open_the_form_when_the_report_fails()
    {
        File.WriteAllText(Reports, "in the way");
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.ReportIssueCommand);

        _shell.Urls.Should().BeEmpty();
        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Be(Strings.Report_Failed);
        _notifier.Errors.Should().ContainSingle().Which.Message.Should().Be(Strings.Report_Failed);
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task The_issue_command_does_not_open_the_form_when_the_report_times_out()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);

        var running = Run(vm.ReportIssueCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await AdvanceUntilDone(running, ReportViewModel.ReportTimeout);

        _shell.Urls.Should().BeEmpty();
        _status.Message.Should().Be(Strings.Report_TimedOut);
        _notifier.Errors.Should().ContainSingle().Which.Message.Should().Be(Strings.Report_TimedOut);
        vm.IsBusy.Should().BeFalse();
        gate.Set();
        await WaitForTheAbandonedWork();
    }

    [Fact]
    public async Task While_the_issue_report_runs_a_second_issue_start_is_refused()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);

        var running = Run(vm.ReportIssueCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        vm.IsBusy.Should().BeTrue();
        vm.ReportIssueCommand.CanExecute(null).Should().BeFalse();
        vm.ReportIssueCommand.Execute(null);
        gate.Set();
        await running;

        sources.InterfaceReads.Should().Be(1);
        Directory.GetFiles(Reports).Should().ContainSingle();
        _shell.Urls.Should().ContainSingle();
        vm.ReportIssueCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task The_issue_command_never_asks_a_question()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        await Run(vm.ReportIssueCommand);

        _confirm.Questions.Should().BeEmpty();
        sources.Wmi2Reads.Should().Be(0);
        vm.LastReportPath.Should().NotBeNull();
    }

    [Fact]
    public async Task A_private_name_in_the_model_does_not_reach_the_address()
    {
        var sources = new FakeReportSources { Model = "ZEKI-PC P65" };
        var vm = ViewModel(sources, null, "ZEKI-PC");
        await Run(vm.CreateCommand);

        vm.ReportIssueCommand.Execute(null);

        var url = _shell.Urls.Single().AbsoluteUri;
        url.Should().NotContainEquivalentOf("ZEKI").And.NotContain("model=").And.Contain("firmware=16Q4EMS2.107");
    }

    [Fact]
    public async Task Serial_user_and_machine_names_are_never_in_the_address()
    {
        var sources = new FakeReportSources { Windows = "Windows 11 ZEKI-PC zeki" };
        var vm = ViewModel(sources, null, "ZEKI-PC", "zeki");
        await Run(vm.CreateCommand);

        vm.ReportIssueCommand.Execute(null);

        _shell.Urls.Single().AbsoluteUri.Should().NotContainEquivalentOf("zeki").And.NotContain("MS-16Q4").And.NotContain("i7");
    }

    [Fact]
    public async Task A_launcher_that_fails_is_a_warning_without_path_not_a_crash()
    {
        _shell.Fails = true;
        var vm = ViewModel(new FakeReportSources());
        await Run(vm.CreateCommand);

        vm.OpenFolderCommand.Execute(null);
        var folderWarning = _status.Message;
        vm.ReportIssueCommand.Execute(null);

        _status.MessageKind.Should().Be(MessageKind.Warning);
        folderWarning.Should().NotContain("secret").And.NotContain(@"\");
        _status.Message.Should().NotContain("secret").And.NotContain(@"\");
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR", StringComparison.Ordinal));
    }
}
