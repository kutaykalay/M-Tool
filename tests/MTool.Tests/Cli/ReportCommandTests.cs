using System.IO.Compression;
using MTool.App.Cli;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Tests.Fakes;

namespace MTool.Tests.Cli;

public sealed class ReportCommandTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 13, 40, 0, TimeSpan.FromHours(3));
    private static readonly byte[] DsdtBytes = [0x44, 0x53, 0x44, 0x54, 0x01, 0x02];

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-report-").FullName;
    private readonly List<string> _warnings = [];

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FakeSources : IReportSources
    {
        public WmiInterface? Wmi { get; init; } = WmiInterface.Wmi1;

        public bool ProbeFails { get; init; }

        public bool ProbeThrows { get; init; }

        public bool MsiThrows { get; init; }

        public bool DsdtFails { get; init; }

        public bool SystemFails { get; init; }

        public bool ClassesFail { get; init; }

        public bool EcFails { get; init; }

        public int EcReads { get; private set; }

        public SystemInfo ReadSystem() => SystemFails
            ? throw new InvalidOperationException("WMI yok")
            : new SystemInfo("MSI", "P65 Creator 9SE", "MS-16Q4", "E16Q4IMS.10D", "2019-05-13", "i7-9750H", "Windows 11");

        public WmiProbeOutcome ReadInterface() =>
            ProbeThrows ? throw new TimeoutException("zaman aşımı")
            : ProbeFails ? WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.QuerySoftware, 0x8004100C))
            : WmiProbeOutcome.Answered(Wmi);

        public MsiInfo ReadMsi() => MsiThrows ? throw new InvalidOperationException("kayıt defteri yok") : MsiInfo.Unknown;

        public IReadOnlyList<WmiClassInfo> ReadClasses() =>
            ClassesFail ? throw new InvalidOperationException("sınıf yok") : [new WmiClassInfo("MSI_CPU", ["CPU"])];

        public byte[] ReadDsdt() => DsdtFails ? throw new InvalidOperationException("tablo yok") : DsdtBytes;

        public int Wmi2Reads { get; private set; }

        public Wmi2Readout ReadWmi2()
        {
            Wmi2Reads++;
            return new Wmi2Readout([]);
        }

        public EcReadout ReadEc()
        {
            EcReads++;
            return EcFails
                ? throw new InvalidOperationException("Bu MSI modeli henüz desteklenmiyor")
                : new EcReadout(new FirmwareInfo("16Q4EMS2.107", "05132019"), DeviceMatch.None, "msi-p65-creator-9se",
                    TestLayouts.P65.Capabilities, new FieldScan([], 0));
        }
    }

    private ReportData Collect(FakeSources sources, bool wmi2 = false) => ReportCommand.Collect(sources, "0.9.0", At, _warnings.Add, wmi2);

    [Fact]
    public void Wmi2_packets_are_read_only_when_asked_and_only_on_wmi2()
    {
        var notAsked = new FakeSources { Wmi = WmiInterface.Wmi2 };
        var wmi1 = new FakeSources();
        var asked = new FakeSources { Wmi = WmiInterface.Wmi2 };

        Collect(notAsked).Input.Wmi2.Should().BeNull();
        Collect(wmi1, wmi2: true).Input.Wmi2!.Error.Should().Contain("no WMI2");
        Collect(asked, wmi2: true).Input.Wmi2!.IsOk.Should().BeTrue();

        notAsked.Wmi2Reads.Should().Be(0);
        wmi1.Wmi2Reads.Should().Be(0);
        asked.Wmi2Reads.Should().Be(1);
    }

    [Fact]
    public void The_report_flags_parse()
    {
        ReportCommand.TryParse(["--report"], out var plain).Should().BeTrue();
        plain.Should().BeFalse();
        ReportCommand.TryParse(["--report", "--wmi2"], out var withWmi2).Should().BeTrue();
        withWmi2.Should().BeTrue();
        ReportCommand.TryParse(["--report", "--wmi3"], out _).Should().BeFalse();
        ReportCommand.TryParse(["--wmi2"], out _).Should().BeFalse();
    }

    [Fact]
    public void Collects_every_part()
    {
        var data = Collect(new FakeSources());

        data.Input.Computer.Value!.Model.Should().Be("P65 Creator 9SE");
        data.Input.Interface.Should().Be(WmiProbeOutcome.Answered(WmiInterface.Wmi1));
        data.Input.Classes.Value.Should().ContainSingle().Which.Name.Should().Be("MSI_CPU");
        data.Input.DsdtBytes.Value.Should().Be(DsdtBytes.Length);
        data.Input.Ec.IsOk.Should().BeTrue();
        data.Dsdt.Should().Equal(DsdtBytes);
        _warnings.Should().BeEmpty();
    }

    [Fact]
    public void A_failing_part_is_reported_logged_and_the_others_still_come()
    {
        var data = Collect(new FakeSources { SystemFails = true, DsdtFails = true });

        data.Input.Computer.Error.Should().Contain("WMI yok");
        data.Input.DsdtBytes.Error.Should().Contain("tablo yok");
        data.Dsdt.Should().BeNull();
        data.Input.Classes.IsOk.Should().BeTrue();
        data.Input.Ec.IsOk.Should().BeTrue();
        _warnings.Should().HaveCount(2);
        _warnings.Should().Contain(w => w.StartsWith("Report: system information unreadable: ", StringComparison.Ordinal));
        _warnings.Should().Contain(w => w.StartsWith("Report: DSDT unreadable: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failing_ec_or_class_list_leaves_the_rest_of_the_report()
    {
        var data = Collect(new FakeSources { EcFails = true, ClassesFail = true });

        data.Input.Ec.Error.Should().Contain("henüz desteklenmiyor");
        data.Input.Classes.Error.Should().Contain("sınıf yok");
        data.Input.Computer.IsOk.Should().BeTrue();
        data.Input.DsdtBytes.IsOk.Should().BeTrue();
    }

    [Fact]
    public void The_ec_is_not_read_on_wmi2()
    {
        var sources = new FakeSources { Wmi = WmiInterface.Wmi2 };

        var data = Collect(sources);

        sources.EcReads.Should().Be(0);
        data.Input.Ec.Error.Should().Contain("only through WMI1");
    }

    [Fact]
    public void The_ec_is_not_read_without_msi_wmi()
    {
        var sources = new FakeSources { Wmi = null };

        var data = Collect(sources);

        sources.EcReads.Should().Be(0);
        data.Input.Ec.Error.Should().Contain("no MSI WMI interface");
    }

    [Fact]
    public void An_unknown_interface_leaves_the_ec_alone()
    {
        var sources = new FakeSources { ProbeFails = true };

        var data = Collect(sources);

        sources.EcReads.Should().Be(0);
        data.Input.Interface.Failure.Should().Be(new WmiFailure(WmiProbeStage.QuerySoftware, 0x8004100C));
        data.Input.Ec.Error.Should().Contain("interface unknown");
    }

    [Fact]
    public void An_msi_read_that_throws_is_unknown_and_the_report_goes_on()
    {
        var warnings = new List<string>();

        var data = ReportCommand.Collect(new FakeSources { MsiThrows = true }, "0.9.0", DateTimeOffset.UnixEpoch, warnings.Add);

        data.Input.Msi.Should().Be(MsiInfo.Unknown);
        data.Input.Ec.IsOk.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("kayıt defteri yok"));
    }

    [Fact]
    public void A_probe_that_throws_all_the_same_is_a_failed_connect_and_leaves_the_ec_alone()
    {
        var sources = new FakeSources { ProbeThrows = true };
        var warnings = new List<string>();

        var data = ReportCommand.Collect(sources, "0.9.0", DateTimeOffset.UnixEpoch, warnings.Add);

        sources.EcReads.Should().Be(0);
        data.Input.Interface.Failure!.Stage.Should().Be(WmiProbeStage.Connect);
        data.Input.Interface.Failure.Code.Should().Be(unchecked((uint)new TimeoutException().HResult));
        warnings.Should().Contain(w => w.Contains("zaman aşımı"));
    }

    [Fact]
    public void The_zip_holds_the_report_and_the_dsdt()
    {
        var path = ReportCommand.Save(_folder, Collect(new FakeSources()), "report text", At);

        Path.GetFileName(path).Should().Be("m-tool-report-20261007-134000.zip");
        using var zip = ZipFile.OpenRead(path);
        zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo("report.txt", "dsdt.aml");
        using (var reader = new StreamReader(zip.GetEntry("report.txt")!.Open()))
        {
            reader.ReadToEnd().Should().Be("report text");
        }

        using var dsdt = new MemoryStream();
        zip.GetEntry("dsdt.aml")!.Open().CopyTo(dsdt);
        dsdt.ToArray().Should().Equal(DsdtBytes);
    }

    [Fact]
    public void Without_a_dsdt_the_zip_holds_only_the_report()
    {
        var path = ReportCommand.Save(_folder, Collect(new FakeSources { DsdtFails = true }), "report text", At);

        using var zip = ZipFile.OpenRead(path);
        zip.Entries.Select(e => e.FullName).Should().Equal("report.txt");
    }

    [Fact]
    public void The_file_name_does_not_follow_the_culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
        try
        {
            Path.GetFileName(ReportCommand.Save(_folder, Collect(new FakeSources()), "x", At))
                .Should().Be("m-tool-report-20261007-134000.zip");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Fact]
    public void While_the_zip_is_written_the_final_name_does_not_exist_yet_and_afterwards_only_it_does()
    {
        var path = Path.Combine(_folder, "r.zip");
        string[]? during = null;
        bool finalExistedDuring = true;

        ReportCommand.WriteNew(path, stream =>
        {
            stream.Write([1, 2, 3]);
            stream.Flush();
            finalExistedDuring = File.Exists(path);
            during = Directory.GetFiles(_folder);
        });

        finalExistedDuring.Should().BeFalse("a half-written zip must never carry the final name");
        during.Should().ContainSingle().Which.Should().EndWith(".tmp");
        Directory.GetFiles(_folder).Should().Equal(path);
        File.ReadAllBytes(path).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void A_write_that_fails_halfway_leaves_neither_the_final_name_nor_a_temporary_file()
    {
        var path = Path.Combine(_folder, "r.zip");

        var write = () => ReportCommand.WriteNew(path, stream =>
        {
            stream.Write([1, 2, 3]);
            throw new InvalidOperationException("disk gone");
        });

        write.Should().Throw<InvalidOperationException>().WithMessage("disk gone");
        Directory.GetFiles(_folder).Should().BeEmpty();
    }

    [Fact]
    public void A_final_file_that_exists_is_never_overwritten_and_no_temporary_file_stays()
    {
        var path = Path.Combine(_folder, "r.zip");
        File.WriteAllBytes(path, [9, 9]);

        var write = () => ReportCommand.WriteNew(path, stream => stream.Write([1, 2, 3]));

        write.Should().Throw<IOException>();
        File.ReadAllBytes(path).Should().Equal(9, 9);
        Directory.GetFiles(_folder).Should().Equal(path);
    }

    [Fact]
    public void A_report_that_cannot_be_written_leaves_no_partial_zip()
    {
        var data = Collect(new FakeSources());
        var first = ReportCommand.Save(_folder, data, "x", At);

        var again = () => ReportCommand.Save(_folder, data, "y", At); // same second: the file exists

        again.Should().Throw<IOException>();
        Directory.GetFiles(_folder).Should().Equal(first);
    }
}
