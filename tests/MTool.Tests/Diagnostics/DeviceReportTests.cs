using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Tests.Fakes;

namespace MTool.Tests.Diagnostics;

public class DeviceReportTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 13, 40, 0, TimeSpan.FromHours(3));

    private static readonly SystemInfo P65System = new(
        Manufacturer: "Micro-Star International Co., Ltd.",
        Model: "P65 Creator 9SE",
        BaseBoard: "MS-16Q4",
        BiosVersion: "E16Q4IMS.10D",
        BiosDate: "2019-05-13",
        Cpu: "Intel(R) Core(TM) i7-9750H CPU @ 2.60GHz",
        Windows: "Microsoft Windows 11 Home 10.0.26300");

    private static readonly EcReadout P65Ec = new(
        new FirmwareInfo("16Q4EMS2.107", "05132019"),
        new DeviceMatch(MatchKind.Exact, "msi-p65-creator-9se", "MSI P65 Creator 9SE"),
        "msi-p65-creator-9se",
        TestLayouts.P65.Capabilities,
        new FieldScan([new FieldReadout(0x68, "MSI_CPU[1]", 0x3A), new FieldReadout(0x6A, "MSI_CPU[5]", null)], Skipped: 0));

    private static DeviceReportInput Input(
        WmiInterface? wmi = WmiInterface.Wmi1, Section<int>? dsdt = null, Section<EcReadout>? ec = null) => new(
        AppVersion: "0.9.0+abc",
        At: At,
        Computer: Section.Ok(P65System),
        Interface: Section.Ok(wmi),
        Classes: Section.Ok<IReadOnlyList<WmiClassInfo>>([new WmiClassInfo("MSI_CPU", ["CPU", "InstanceName"]), new WmiClassInfo("MSI_VGA", ["VGA"])]),
        DsdtBytes: dsdt ?? Section.Ok(240_896),
        Ec: ec ?? Section.Ok(P65Ec));

    [Fact]
    public void Lists_the_system_the_interface_the_classes_and_the_dsdt()
    {
        var text = DeviceReport.Format(Input(), privateWords: []);

        text.Should().Contain("Model        : P65 Creator 9SE")
            .And.Contain("Baseboard    : MS-16Q4")
            .And.Contain("BIOS         : E16Q4IMS.10D (2019-05-13)")
            .And.Contain("Interface    : WMI1")
            .And.Contain("MSI_CPU: CPU, InstanceName")
            .And.Contain("DSDT         : 240896 bytes (dsdt.aml)");
    }

    [Fact]
    public void Writes_the_match_the_capabilities_and_the_mapped_fields()
    {
        var text = DeviceReport.Format(Input(), privateWords: []);

        text.Should().Contain("Firmware     : 16Q4EMS2.107 (05132019)")
            .And.Contain("Match        : Exact (msi-p65-creator-9se)")
            .And.Contain("Capabilities : GPU fan, fan curve, Cooler Boost, charge limit; modes High, Balanced, Eco")
            .And.Contain("0x68 MSI_CPU[1] = 0x3A")
            .And.Contain("0x6A MSI_CPU[5] = read failed")
            .And.NotContain("unverified");
    }

    [Fact]
    public void Values_read_with_another_model_s_layout_are_marked_unverified()
    {
        var ec = P65Ec with { Firmware = new FirmwareInfo("1541EMS1.115", "01012020"), Match = DeviceMatch.None };

        var text = DeviceReport.Format(Input(ec: Section.Ok(ec)), privateWords: []);

        text.Should().Contain("Match        : none")
            .And.Contain("Read with    : msi-p65-creator-9se layout, unverified on this model");
    }

    [Fact]
    public void An_unreadable_dsdt_is_reported_and_the_rest_still_comes()
    {
        var text = DeviceReport.Format(Input(dsdt: Section.Failed<int>("GetSystemFirmwareTable 0")), privateWords: []);

        text.Should().Contain("DSDT         : not read (GetSystemFirmwareTable 0)")
            .And.Contain("Firmware     : 16Q4EMS2.107");
    }

    [Theory]
    [InlineData(WmiInterface.Wmi2, "WMI2")]
    [InlineData(null, "none")]
    public void Without_wmi1_the_ec_section_says_why_it_was_skipped(WmiInterface? wmi, string name)
    {
        var text = DeviceReport.Format(
            Input(wmi, ec: Section.Failed<EcReadout>("not read: M-Tool reads the EC only through WMI1 so far")), privateWords: []);

        text.Should().Contain($"Interface    : {name}")
            .And.Contain("EC           : not read: M-Tool reads the EC only through WMI1 so far")
            .And.Contain("MSI_CPU: CPU, InstanceName");
    }

    [Fact]
    public void A_failed_wmi_probe_is_reported()
    {
        var input = Input() with { Interface = Section.Failed<WmiInterface?>("WMI zaman aşımı"), Classes = Section.Failed<IReadOnlyList<WmiClassInfo>>("WMI zaman aşımı") };

        var text = DeviceReport.Format(input, privateWords: []);

        text.Should().Contain("Interface    : not read (WMI zaman aşımı)")
            .And.Contain("Classes      : not read (WMI zaman aşımı)");
    }

    [Fact]
    public void Masks_the_user_and_machine_names_wherever_they_appear()
    {
        var input = Input() with
        {
            Computer = Section.Ok(P65System with { Model = "KUTAY-LAPTOP special", BaseBoard = "kutay" }),
            Ec = Section.Failed<EcReadout>(@"C:\Users\kutay\AppData failed"),
        };

        var text = DeviceReport.Format(input, privateWords: ["kutay", "KUTAY-LAPTOP"]);

        text.Should().NotContainEquivalentOf("kutay")
            .And.Contain("Model        : <hidden> special")
            .And.Contain(@"C:\Users\<hidden>\AppData failed");
    }

    [Fact]
    public void Short_or_empty_private_words_do_not_mask_everything()
    {
        var text = DeviceReport.Format(Input(), privateWords: ["", " ", "a"]);

        text.Should().Contain("Model        : P65 Creator 9SE").And.NotContain("<hidden>");
    }

    [Fact]
    public void Has_a_header_with_the_version_and_time()
    {
        var text = DeviceReport.Format(Input(), privateWords: []);

        text.Should().StartWith("M-Tool device report")
            .And.Contain("M-Tool       : 0.9.0+abc")
            .And.Contain("Generated    : 2026-10-07 13:40 +03:00")
            .And.Contain("Windows      : Microsoft Windows 11 Home 10.0.26300");
    }

    [Fact]
    public void A_scan_that_stopped_early_says_how_many_fields_it_skipped()
    {
        var ec = P65Ec with { Fields = P65Ec.Fields with { Skipped = 27 } };

        DeviceReport.Format(Input(ec: Section.Ok(ec)), privateWords: [])
            .Should().Contain("  27 more fields skipped after 3 failed reads in a row");
    }

    [Fact]
    public void An_unreadable_system_section_is_reported_once()
    {
        var text = DeviceReport.Format(Input() with { Computer = Section.Failed<SystemInfo>("WMI yok") }, privateWords: []);

        text.Should().Contain("System       : not read (WMI yok)").And.Contain("Interface    : WMI1");
        text.Split("not read (WMI yok)").Should().HaveCount(2);
    }

    [Fact]
    public void No_msi_classes_is_written_as_none()
    {
        var text = DeviceReport.Format(Input() with { Classes = Section.Ok<IReadOnlyList<WmiClassInfo>>([]) }, privateWords: []);

        text.Should().Contain("Classes      : none");
    }

    [Fact]
    public void The_windows_line_belongs_to_the_system_section()
    {
        var text = DeviceReport.Format(Input(), privateWords: []);

        text.IndexOf("[System]", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("Windows      :", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_section_always_has_a_reason()
    {
        Section.Failed<int>("  ").Error.Should().Be("unknown error");
    }
}
