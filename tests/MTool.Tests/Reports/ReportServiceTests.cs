using System.IO.Compression;
using Microsoft.Extensions.Time.Testing;
using MTool.App.Reports;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

public sealed class ReportServiceTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 9, 30, 15, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Directory.CreateTempSubdirectory("mtool-report-").FullName, "reports");
    private readonly ListLog _log = new();

    public void Dispose() => Directory.Delete(Directory.GetParent(_folder)!.FullName, recursive: true);

    private ReportService Service(FakeReportSources sources, params string[] privateWords)
    {
        var time = new FakeTimeProvider(At);
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return new ReportService(sources, _log, _folder, "0.9.0", privateWords, time);
    }

    private static string ReadEntry(string zipPath, string name)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_zip_holds_report_txt_and_dsdt_aml_in_the_reports_folder()
    {
        var result = Service(new FakeReportSources()).Create();

        Path.GetDirectoryName(result.Path).Should().Be(_folder);
        Path.GetFileName(result.Path).Should().Be("m-tool-report-20261012-093015.zip");
        using (var zip = ZipFile.OpenRead(result.Path))
        {
            zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo("report.txt", "dsdt.aml");
        }

        ReadEntry(result.Path, "report.txt").Should().Be(result.Text);
        result.Text.Should().StartWith("M-Tool device report").And.Contain("P65 Creator 9SE");
    }

    [Fact]
    public void Without_a_dsdt_only_report_txt_is_in_the_zip()
    {
        var result = Service(new FakeReportSources { DsdtFails = true }).Create();

        using var zip = ZipFile.OpenRead(result.Path);
        zip.Entries.Select(e => e.FullName).Should().Equal("report.txt");
    }

    [Fact]
    public void A_part_that_cannot_be_read_is_written_as_not_read_and_the_report_still_comes()
    {
        var result = Service(new FakeReportSources { SystemFails = true, DsdtFails = true }).Create();

        result.Text.Should().Contain("not read (InvalidOperationException: WMI yok)");
        File.Exists(result.Path).Should().BeTrue();
        _log.Lines.Should().Contain(l => l.StartsWith("WARN Report: system information unreadable: ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_plain_report_never_reads_wmi2_even_on_a_wmi2_laptop()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };

        var result = Service(sources).Create();

        sources.Wmi2Reads.Should().Be(0);
        result.Text.Should().NotContain("Get_EC");
    }

    [Fact]
    public void The_wmi2_report_reads_the_packets_exactly_once_on_a_wmi2_laptop()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2 };

        var result = Service(sources).CreateWithWmi2();

        sources.Wmi2Reads.Should().Be(1);
        result.Text.Should().Contain("Get_EC");
    }

    [Fact]
    public void The_wmi2_report_reads_nothing_of_wmi2_on_a_wmi1_laptop()
    {
        var sources = new FakeReportSources();

        Service(sources).CreateWithWmi2();

        sources.Wmi2Reads.Should().Be(0);
    }

    [Fact]
    public void User_and_machine_names_are_hidden_in_the_report_and_the_zip()
    {
        var sources = new FakeReportSources { Windows = "Windows 11 of DESKTOP-KUTAY and kutaykalay" };

        var result = Service(sources, "DESKTOP-KUTAY", "kutaykalay").Create();

        result.Text.Should().NotContainEquivalentOf("DESKTOP-KUTAY").And.NotContainEquivalentOf("kutaykalay").And.Contain("<hidden>");
        ReadEntry(result.Path, "report.txt").Should().NotContainEquivalentOf("kutaykalay");
    }

    [Fact]
    public void A_zip_that_cannot_be_written_throws_and_leaves_no_partial_file()
    {
        var service = Service(new FakeReportSources());
        var first = service.Create().Path;

        var again = () => service.Create(); // same second: the file exists

        again.Should().Throw<IOException>();
        Directory.GetFiles(_folder).Should().Equal(first);
    }

    [Theory]
    [InlineData("\uFF3A\uFF25\uFF2B\uFF29-PC P65", "ZEKI-PC")] // fullwidth letters in the value
    [InlineData("ZEKI-PC P65", "ZEKI-PC")]
    [InlineData("zeki-pc P65", "ZEKI-PC")]
    [InlineData("P65 \u212A-PC", "K-PC")] // Kelvin sign, NFKC-equal to K
    [InlineData("ZEKI-PC P65", "\uFF3A\uFF25\uFF2B\uFF29-PC")] // fullwidth letters in the name
    public void A_private_name_in_a_look_alike_form_is_kept_out_of_model_and_firmware(string model, string word)
    {
        var sources = new FakeReportSources { Model = model, Firmware = model };

        var result = Service(sources, word).Create();

        result.Model.Should().BeNull();
        result.Firmware.Should().BeNull();
    }

    [Fact]
    public void A_model_without_a_private_name_survives_the_comparison()
    {
        var result = Service(new FakeReportSources(), "ZEKI-PC", "\uFF3A\uFF25").Create();

        result.Model.Should().Be("P65 Creator 9SE");
    }

    [Fact]
    public void A_model_with_a_broken_surrogate_is_not_used_and_does_not_crash_the_report()
    {
        var result = Service(new FakeReportSources { Model = "P65\uD800 Creator" }, "ZEKI-PC").Create();

        result.Model.Should().BeNull();
        File.Exists(result.Path).Should().BeTrue();
    }

    [Fact]
    public void A_wmi2_part_that_cannot_be_read_is_logged_in_english()
    {
        var sources = new FakeReportSources { Wmi = WmiInterface.Wmi2, Wmi2Fails = true };

        var result = Service(sources).CreateWithWmi2();

        _log.Lines.Should().Contain(l => l.Contains("WMI2 packets unreadable", StringComparison.Ordinal));
        result.Text.Should().Contain("InvalidOperationException: no answer");
    }

    [Fact]
    public void A_reports_folder_that_cannot_be_made_throws()
    {
        File.WriteAllText(_folder, "in the way");

        var create = () => Service(new FakeReportSources()).Create();

        create.Should().Throw<IOException>();
    }

    [Fact]
    public void The_result_carries_the_model_and_the_firmware_from_the_report()
    {
        var result = Service(new FakeReportSources()).Create();

        result.Model.Should().Be("P65 Creator 9SE");
        result.Firmware.Should().Be("16Q4EMS2.107");
    }

    [Fact]
    public void Unread_model_and_firmware_are_null()
    {
        var result = Service(new FakeReportSources { Model = null, Firmware = null }).Create();

        result.Model.Should().BeNull();
        result.Firmware.Should().BeNull();
    }

    [Fact]
    public void A_model_that_contains_a_private_name_is_dropped_from_the_result()
    {
        var result = Service(new FakeReportSources { Model = "Kutay-PC P65" }, "Kutay-PC").Create();

        result.Model.Should().BeNull();
        result.Firmware.Should().Be("16Q4EMS2.107");
    }

    [Fact]
    public void A_written_report_is_logged_in_English()
    {
        var result = Service(new FakeReportSources()).Create();

        _log.Lines.Should().Contain($"INFO Report written: {result.Path}");
    }

    [Theory]
    [InlineData(WmiInterface.Wmi2, true)]
    [InlineData(WmiInterface.Wmi1, false)]
    [InlineData(null, false)]
    public void Wmi2_is_detected_from_the_interface_probe_alone(WmiInterface? wmi, bool expected)
    {
        var sources = new FakeReportSources { Wmi = wmi };

        Service(sources).HasWmi2Interface().Should().Be(expected);

        sources.Wmi2Reads.Should().Be(0);
        sources.EcReads.Should().Be(0);
    }

    [Fact]
    public void A_failing_probe_means_no_wmi2_and_is_logged()
    {
        var sources = new FakeReportSources { ProbeFails = true };

        Service(sources).HasWmi2Interface().Should().BeFalse();

        _log.Lines.Should().Contain(l => l.StartsWith("WARN", StringComparison.Ordinal));
    }
}
