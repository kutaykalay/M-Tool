using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

/// <summary>How the opt-in WMI2 section reads in report.txt (7g).</summary>
public class DeviceReportWmi2Tests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 13, 40, 0, TimeSpan.FromHours(3));

    private static DeviceReportInput Input(WmiInterface? wmi, Section<Wmi2Readout>? wmi2) => new(
        AppVersion: "0.9.0",
        At: At,
        Computer: Section.Ok(new SystemInfo("MSI", "Katana GF66", "MS-1581", "E1581IMS.10C", "2022-05-13", "i7-11800H", "Windows 11")),
        Interface: Section.Ok(wmi),
        Classes: Section.Ok<IReadOnlyList<WmiClassInfo>>([new WmiClassInfo("MSI_ACPI", ["Get_EC"])]),
        DsdtBytes: Section.Ok(300_000),
        Ec: Section.Failed<EcReadout>("not read: M-Tool reads the EC only through WMI1 so far"),
        Wmi2: wmi2);

    private static Wmi2Readout Readout()
    {
        var ok = new byte[Wmi2Probe.PacketLength];
        ok[0] = 1;
        ok[1] = 61;
        ok[2] = 48;
        return new Wmi2Readout([
            new Wmi2Call("Get_Temperature", 0, ok, null),
            new Wmi2Call("Get_Thermal", 1, null, "TimeoutException: zaman aşımı"),
        ]);
    }

    [Fact]
    public void Raw_packets_are_written_in_hex_with_failures_named()
    {
        var text = DeviceReport.Format(Input(WmiInterface.Wmi2, Section.Ok(Readout())), privateWords: []);

        text.Should().Contain("[WMI2, Get_* methods only, raw]")
            .And.Contain("  Get_Temperature(0) : 01 3D 30 00")
            .And.Contain("  Get_Thermal(1) : failed (TimeoutException: zaman aşımı)");
    }

    [Fact]
    public void The_reading_is_marked_as_an_unverified_guess()
    {
        var text = DeviceReport.Format(Input(WmiInterface.Wmi2, Section.Ok(Readout())), privateWords: []);

        text.Should().Contain("Reading      : YAMDCC's packet layout, unverified")
            .And.Contain("Temperatures : CPU 61 C, GPU 48 C")
            .And.Contain("Firmware     : ?");
    }

    [Fact]
    public void A_wmi2_laptop_without_the_flag_is_told_how_to_add_it()
    {
        DeviceReport.Format(Input(WmiInterface.Wmi2, wmi2: null), privateWords: [])
            .Should().Contain("not read: run M-Tool.exe --report --wmi2 to include the WMI2 packets");
    }

    [Fact]
    public void A_wmi1_laptop_has_no_wmi2_section()
    {
        DeviceReport.Format(Input(WmiInterface.Wmi1, wmi2: null), privateWords: []).Should().NotContain("WMI2, Get_*");
    }
}
