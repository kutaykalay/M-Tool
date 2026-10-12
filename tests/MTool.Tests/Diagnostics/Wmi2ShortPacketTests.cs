using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

/// <summary>A WMI2 answer shorter than 32 bytes must be reported as such and must never fail the report.</summary>
public class Wmi2ShortPacketTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 13, 40, 0, TimeSpan.FromHours(3));

    private static byte[] Packet(int length)
    {
        var packet = new byte[length];
        if (length > 0)
        {
            packet[0] = Wmi2Probe.Success;
        }

        for (var i = 1; i < length; i++)
        {
            packet[i] = (byte)(0x40 + i);
        }

        return packet;
    }

    private static Wmi2Readout Readout(int length) => new([
        new Wmi2Call("Get_EC", 0, Packet(length), null),
        new Wmi2Call("Get_Temperature", 0, Packet(length), null),
        new Wmi2Call("Get_Fan", 0, Packet(length), null),
    ]);

    private static DeviceReportInput Input(Wmi2Readout readout) => new(
        AppVersion: "0.9.0",
        At: At,
        Computer: Section.Ok(new SystemInfo("MSI", "Katana GF66", "MS-1581", "E1581IMS.10C", "2022-05-13", "i7-11800H", "Windows 11")),
        Interface: Section.Ok<WmiInterface?>(WmiInterface.Wmi2),
        Classes: Section.Ok<IReadOnlyList<WmiClassInfo>>([new WmiClassInfo("MSI_ACPI", ["Get_EC"])]),
        DsdtBytes: Section.Ok(300_000),
        Ec: Section.Failed<EcReadout>("not read: M-Tool reads the EC only through WMI1 so far"),
        Wmi2: Section.Ok(readout));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(31)]
    public void A_short_packet_gives_no_reading_instead_of_an_exception(int length)
    {
        var read = () => Readout(length).Interpret();

        var reading = read.Should().NotThrow().Subject;
        reading.Firmware.Should().BeNull();
        reading.FirmwareDate.Should().BeNull();
        reading.CpuTempC.Should().BeNull();
        reading.GpuTempC.Should().BeNull();
        reading.CpuRpm.Should().BeNull();
        reading.GpuRpm.Should().BeNull();
    }

    [Fact]
    public void A_short_packet_is_named_in_the_reading_with_its_length()
    {
        var reading = Readout(3).Interpret();

        reading.Problem.Should().Contain("Get_EC(0)").And.Contain("3 bytes").And.Contain("32");
    }

    [Fact]
    public void Only_the_short_packets_are_unreadable_the_others_still_read()
    {
        var temperatures = Packet(Wmi2Probe.PacketLength);
        temperatures[1] = 61;
        temperatures[2] = 48;
        var readout = new Wmi2Readout([
            new Wmi2Call("Get_EC", 0, Packet(5), null),
            new Wmi2Call("Get_Temperature", 0, temperatures, null),
        ]);

        var reading = readout.Interpret();

        reading.Firmware.Should().BeNull();
        reading.CpuTempC.Should().Be(61);
        reading.GpuTempC.Should().Be(48);
        reading.Problem.Should().Contain("Get_EC(0)").And.NotContain("Get_Temperature");
    }

    [Fact]
    public void Whole_packets_have_no_problem()
    {
        Readout(Wmi2Probe.PacketLength).Interpret().Problem.Should().BeNull();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(31)]
    public void The_report_with_a_short_packet_is_still_made_and_says_the_packet_was_short(int length)
    {
        var make = () => DeviceReport.Format(Input(Readout(length)), privateWords: []);

        var text = make.Should().NotThrow().Subject;
        text.Should().Contain("Get_EC(0) : 01 ")
            .And.Contain("Unreadable")
            .And.Contain($"{length} bytes")
            .And.Contain("Temperatures : CPU ? C, GPU ? C");
    }
}
