using MTool.Core.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using MTool.App.Reports;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

public sealed class ReportServiceSubmissionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Directory.CreateTempSubdirectory("mtool-submission-").FullName, "reports");
    private readonly ListLog _log = new();

    public void Dispose() => Directory.Delete(Directory.GetParent(_folder)!.FullName, recursive: true);

    private ReportService Service(FakeReportSources sources, params string[] privateWords) =>
        new(sources, _log, _folder, "0.12.0", privateWords, new FakeTimeProvider());

    [Fact]
    public void Collecting_for_a_submission_reads_no_DSDT_and_writes_no_zip()
    {
        var sources = new FakeReportSources();

        Service(sources).CollectForSubmission(includeWmi2: false);

        sources.DsdtReads.Should().Be(0);
        Directory.Exists(_folder).Should().BeFalse();
    }

    [Fact]
    public void The_plain_report_still_reads_the_DSDT()
    {
        var sources = new FakeReportSources();

        Service(sources).Create();

        sources.DsdtReads.Should().Be(1);
    }

    [Fact]
    public void The_text_is_the_submission_json_with_the_names_hidden()
    {
        var json = Service(new FakeReportSources { Model = "Kutay P65" }, "Kutay").CollectForSubmission(includeWmi2: false);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("schemaVersion").GetInt32().Should().Be(EcSubmission.SchemaVersion);
        root.GetProperty("app").GetProperty("version").GetString().Should().Be("0.12.0");
        root.GetProperty("system").GetProperty("model").GetString().Should().Be("<hidden> P65");
        root.GetProperty("ec").GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public void On_a_WMI1_laptop_WMI2_is_never_called_even_when_asked()
    {
        var sources = new FakeReportSources();

        var json = Service(sources).CollectForSubmission(includeWmi2: true);

        sources.Wmi2Reads.Should().Be(0);
        JsonDocument.Parse(json).RootElement.GetProperty("wmi2").GetProperty("status").GetString().Should().Be("failed");
    }

    [Fact]
    public void On_a_WMI2_laptop_WMI2_is_called_once_only_when_asked()
    {
        var asked = new FakeReportSources { Wmi = WmiInterface.Wmi2 };
        var notAsked = new FakeReportSources { Wmi = WmiInterface.Wmi2 };

        var json = Service(asked).CollectForSubmission(includeWmi2: true);
        Service(notAsked).CollectForSubmission(includeWmi2: false);

        asked.Wmi2Reads.Should().Be(1);
        notAsked.Wmi2Reads.Should().Be(0);
        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("wmi2").GetProperty("status").GetString().Should().Be("ok");
        root.GetProperty("ec").GetProperty("status").GetString().Should().Be("not_read");
    }

    [Fact]
    public void A_failed_part_is_logged_and_the_submission_is_still_made()
    {
        var json = Service(new FakeReportSources { SystemFails = true }).CollectForSubmission(includeWmi2: false);

        JsonDocument.Parse(json).RootElement.GetProperty("system").GetProperty("model").ValueKind.Should().Be(JsonValueKind.Null);
        _log.Lines.Should().Contain(l => l.StartsWith("WARN ") && l.Contains("system information"));
    }
}
