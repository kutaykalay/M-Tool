using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Time.Testing;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

public sealed class ReportViewModelSubmitTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mtool-submit-vm-").FullName;
    private readonly FakeNotifier _notifier = new();
    private readonly FakeConfirm _confirm = new();
    private readonly FakeSubmissionPreview _preview = new();
    private readonly FakeHttpHandler _http = new();
    private readonly ListLog _log = new();
    private readonly StatusViewModel _status;
    private readonly FakeTimeProvider _time = new();
    private readonly SubmissionClient _client;

    public ReportViewModelSubmitTests()
    {
        _status = new StatusViewModel(_notifier, new ImmediateDispatcher(), TimeProvider.System);
        _client = new SubmissionClient(_http, SubmissionEndpoint.Uri, "0.12.0", SubmissionClient.DefaultTimeout);
    }

    public void Dispose()
    {
        _client.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private ReportViewModel ViewModel(FakeReportSources sources, CancellationToken stop = default) =>
        new(new ReportService(sources, _log, Path.Combine(_root, "reports"), "0.12.0", ["Kutay"]),
            _confirm, _preview, _client, new FakeShellLauncher(), _status, _notifier, _log, _time, stop);

    private static Task Run(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        return ((IAsyncRelayCommand)command).ExecutionTask ?? Task.CompletedTask;
    }

    private void Answer(HttpStatusCode status, string body) =>
        _http.Answer = (_, _) => Task.FromResult(FakeHttpHandler.Json(status, body));

    [Fact]
    public async Task Declined_in_the_preview_sends_nothing()
    {
        _preview.Answer = false;
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.SendEcDataCommand);

        _preview.Shown.Should().ContainSingle();
        _http.Requests.Should().BeEmpty();
        _notifier.Errors.Should().BeEmpty();
        _log.Lines.Should().Contain("INFO EC submission: not sent (declined in preview).");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Sending_posts_exactly_the_previewed_text_once()
    {
        var vm = ViewModel(new FakeReportSources { Model = "Kutay P65" });

        await Run(vm.SendEcDataCommand);

        var shown = _preview.Shown.Should().ContainSingle().Subject;
        shown.Should().Contain("\"model\": \"<hidden> P65\"").And.NotContain("Kutay");
        _http.Requests.Should().ContainSingle().Which.Body.Should().Equal(Encoding.UTF8.GetBytes(shown));
    }

    [Fact]
    public async Task On_a_WMI1_laptop_no_WMI2_question_is_asked_and_the_data_is_previewed()
    {
        var vm = ViewModel(new FakeReportSources());
        await vm.LoadAsync();

        await Run(vm.SendEcDataCommand);

        _confirm.Questions.Should().BeEmpty();
        _preview.Shown.Should().ContainSingle();
    }

    [Fact]
    public async Task On_a_WMI2_laptop_no_to_the_question_reads_and_sends_nothing()
    {
        _confirm.Answer = false;
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        await Run(vm.SendEcDataCommand);

        _confirm.Questions.Should().ContainSingle().Which.Should().Be(Strings.Submit_Wmi2Confirm);
        _confirm.DefaultsToYes.Should().ContainSingle().Which.Should().BeFalse();
        sources.Wmi2Reads.Should().Be(0);
        sources.EcReads.Should().Be(0);
        _preview.Shown.Should().BeEmpty();
        _http.Requests.Should().BeEmpty();
        _log.Lines.Should().Contain("INFO EC submission: WMI2 not confirmed, nothing was read or sent.");
    }

    [Fact]
    public async Task On_a_WMI2_laptop_yes_reads_WMI2_once_and_previews_it()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var vm = ViewModel(sources);
        await vm.LoadAsync();

        await Run(vm.SendEcDataCommand);

        sources.Wmi2Reads.Should().Be(1);
        _preview.Shown.Single().Should().Contain("\"method\": \"Get_EC\"");
        _http.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task While_the_preview_is_open_no_report_action_can_start()
    {
        var vm = ViewModel(new FakeReportSources());
        bool[]? whileOpen = null;
        _preview.WhileOpen = () => whileOpen =
            [vm.SendEcDataCommand.CanExecute(null), vm.CreateCommand.CanExecute(null), vm.ReportIssueCommand.CanExecute(null)];

        await Run(vm.SendEcDataCommand);

        whileOpen.Should().Equal(false, false, false);
        vm.SendEcDataCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Created_thanks_with_the_record_id()
    {
        Answer(HttpStatusCode.Created, """{"status":"created","id":"0123456789ab"}""");
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.SendEcDataCommand);

        var expected = $"{Strings.Submit_Sent} {string.Format(Strings.Submit_Record, "0123456789ab")}";
        _notifier.Infos.Select(i => i.Message).Should().Contain(expected);
        _status.MessageKind.Should().Be(MessageKind.Info);
        _status.Message.Should().Be(expected);
        _log.Lines.Should().Contain(l => l.StartsWith("INFO EC submission: sent (") && l.EndsWith(" bytes), id 0123456789ab."));
    }

    [Fact]
    public async Task Duplicate_thanks_without_an_id_when_none_came()
    {
        Answer(HttpStatusCode.OK, """{"status":"duplicate"}""");
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.SendEcDataCommand);

        _status.Message.Should().Be(Strings.Submit_Duplicate);
        _log.Lines.Should().Contain("INFO EC submission: duplicate, id none.");
    }

    public static TheoryData<int, string, string, string> Failures => new()
    {
        { 429, """{"status":"error","error":"rate_limited"}""", "Submit_RateLimited", "WARN EC submission refused: 429, retry after 60 s." },
        { 503, """{"status":"error","error":"daily_cap"}""", "Submit_Unavailable", "WARN EC submission failed: HTTP 503 daily_cap." },
        { 502, "", "Submit_Unavailable", "WARN EC submission failed: HTTP 502 -." },
        { 302, "", "Submit_Unavailable", "WARN EC submission failed: HTTP 302 -." },
        { 200, "<html>portal</html>", "Submit_Unavailable", "WARN EC submission failed: HTTP 200 -." },
        { 400, """{"status":"error","error":"bad_schema","path":"ec"}""", "Submit_Rejected", "WARN EC submission rejected: HTTP 400 bad_schema." },
        { 413, """{"status":"error","error":"too_large"}""", "Submit_Rejected", "WARN EC submission rejected: HTTP 413 too_large." },
        { 407, "", "Submit_Network", "WARN EC submission failed: network (HTTP 407)." },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Each_failure_warns_in_the_user_language_and_logs_in_English(int status, string body, string key, string logLine)
    {
        Answer((HttpStatusCode)status, body);
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.SendEcDataCommand);

        var message = key == "Submit_RateLimited" ? string.Format(Strings.Submit_RateLimited, 1) : Strings.ResourceManager.GetString(key)!;
        _status.MessageKind.Should().Be(MessageKind.Warning);
        _status.Message.Should().Be(message);
        _notifier.Errors.Should().ContainSingle().Which.Message.Should().Be(message);
        _log.Lines.Should().Contain(logLine);
    }

    [Fact]
    public async Task Rate_limited_rounds_the_wait_up_to_whole_minutes()
    {
        _http.Answer = (_, _) =>
        {
            var response = FakeHttpHandler.Json((HttpStatusCode)429, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(61));
            return Task.FromResult(response);
        };
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.SendEcDataCommand);

        _status.Message.Should().Be(string.Format(Strings.Submit_RateLimited, 2));
    }

    [Fact]
    public async Task No_network_warns_and_logs_the_kind()
    {
        _http.Answer = (_, _) => throw new System.Net.Http.HttpRequestException("No such host is known.");
        var vm = ViewModel(new FakeReportSources());

        await Run(vm.SendEcDataCommand);

        _status.Message.Should().Be(Strings.Submit_Network);
        _log.Lines.Should().Contain("WARN EC submission failed: network.");
    }

    [Fact]
    public async Task Closing_M_Tool_during_the_send_shows_nothing()
    {
        using var stop = new CancellationTokenSource();
        _http.Answer = async (_, token) =>
        {
            await stop.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK);
        };
        var vm = ViewModel(new FakeReportSources(), stop.Token);

        await Run(vm.SendEcDataCommand);

        _notifier.Errors.Should().BeEmpty();
        _notifier.Infos.Where(i => i.Message != Strings.Submit_Collecting).Should().BeEmpty();
        _log.Lines.Should().Contain("INFO EC submission: cancelled at shutdown.");
    }

    [Fact]
    public async Task A_payload_over_the_limit_is_not_previewed_or_sent()
    {
        var classes = Enumerable.Range(0, 200).Select(i => new WmiClassInfo($"MSI_Class{i}", Enumerable.Range(0, 20).Select(p => $"Property{p}").ToList())).ToList();
        var vm = ViewModel(new FakeReportSources { Classes = classes });

        await Run(vm.SendEcDataCommand);

        _preview.Shown.Should().BeEmpty();
        _http.Requests.Should().BeEmpty();
        _status.Message.Should().Be(Strings.Submit_TooLarge);
        _log.Lines.Should().Contain(l => l.StartsWith("WARN EC submission: payload ") && l.EndsWith($" bytes > {EcSubmission.MaxBytes}, not sent."));
    }

    [Fact]
    public async Task Collecting_that_hangs_times_out_like_the_report()
    {
        using var gate = new ManualResetEventSlim();
        var sources = new FakeReportSources { Gate = gate };
        var vm = ViewModel(sources);

        var running = Run(vm.SendEcDataCommand);
        sources.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        while (!running.IsCompleted)
        {
            _time.Advance(ReportViewModel.ReportTimeout);
            await Task.WhenAny(running, Task.Delay(10));
        }

        _status.Message.Should().Be(Strings.Report_TimedOut);
        _preview.Shown.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
        gate.Set();
    }
}
