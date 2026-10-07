using System.Text;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

/// <summary>The opt-in WMI2 part of <c>--report</c> (7g): raw <c>Get_*</c> packets, a cautious reading.</summary>
public class Wmi2ProbeTests
{
    private static byte[] Packet(params (int Index, byte Value)[] values)
    {
        var packet = new byte[Wmi2Probe.PacketLength];
        packet[0] = 1; // success
        foreach (var (index, value) in values)
        {
            packet[index] = value;
        }

        return packet;
    }

    private static byte[] EcPacket()
    {
        var packet = Packet();
        Encoding.ASCII.GetBytes("1582EMS1.107").CopyTo(packet, 2);
        Encoding.ASCII.GetBytes("05132022 10:20:3").CopyTo(packet, 14);
        return packet;
    }

    private static byte[] Answer(string method, byte sub) => (method, sub) switch
    {
        ("Get_EC", 0) => EcPacket(),
        ("Get_Temperature", 0) => Packet((1, 61), (2, 48)),
        ("Get_Fan", 0) => Packet((1, 0x00), (2, 0x9F), (3, 0x00), (4, 0x00)), // CPU period 159 -> 3006 RPM, GPU stopped
        _ => Packet(),
    };

    [Fact]
    public void Only_get_methods_are_ever_called()
    {
        Wmi2Probe.Calls.Should().NotBeEmpty().And.OnlyContain(c => c.Method.StartsWith("Get_", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_call_is_made_in_order_and_kept_raw()
    {
        var made = new List<(string, byte)>();

        var readout = Wmi2Probe.Run((method, sub) =>
        {
            made.Add((method, sub));
            return Answer(method, sub);
        });

        made.Should().Equal(Wmi2Probe.Calls.Select(c => (c.Method, c.Sub)));
        readout.Calls.Should().HaveCount(Wmi2Probe.Calls.Count).And.OnlyContain(c => c.Packet != null && c.Error == null);
    }

    [Fact]
    public void A_failing_call_is_recorded_and_the_rest_still_run()
    {
        var readout = Wmi2Probe.Run((method, sub) =>
            method == "Get_Thermal" ? throw new TimeoutException("zaman aşımı") : Answer(method, sub));

        readout.Calls.Where(c => c.Method == "Get_Thermal").Should().OnlyContain(c => c.Packet == null && c.Error!.Contains("zaman aşımı"));
        readout.Calls.Single(c => c.Method == "Get_EC").Packet.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void A_packet_that_is_not_32_bytes_is_an_error(int length)
    {
        var readout = Wmi2Probe.Run((_, _) => new byte[length]);

        readout.Calls.Take(Wmi2Probe.MaxFailuresInARow).Should().OnlyContain(c => c.Packet == null && c.Error!.Contains("32"));
        readout.Calls.Skip(Wmi2Probe.MaxFailuresInARow).Should().OnlyContain(c => c.Packet == null && c.Error!.Contains("skipped"));
    }

    [Fact]
    public void Reads_firmware_temperatures_and_rpm_the_way_yamdcc_does()
    {
        var reading = Wmi2Probe.Run(Answer).Interpret();

        reading.Firmware.Should().Be("1582EMS1.107");
        reading.FirmwareDate.Should().Be("05132022 10:20:3");
        reading.CpuTempC.Should().Be(61);
        reading.GpuTempC.Should().Be(48);
        reading.CpuRpm.Should().Be(3006);
        reading.GpuRpm.Should().Be(0);
    }

    [Fact]
    public void A_failed_packet_gives_no_reading()
    {
        var failed = Wmi2Probe.Run((method, sub) =>
        {
            var packet = Answer(method, sub);
            packet[0] = 0; // the method reported failure
            return packet;
        }).Interpret();

        failed.Firmware.Should().BeNull();
        failed.CpuTempC.Should().BeNull();
        failed.CpuRpm.Should().BeNull();
    }

    [Fact]
    public void Control_characters_in_the_firmware_are_not_passed_on()
    {
        var reading = Wmi2Probe.Run((method, sub) =>
        {
            var packet = Answer(method, sub);
            if (method == "Get_EC")
            {
                packet[5] = 0x0A;
            }

            return packet;
        }).Interpret();

        reading.Firmware.Should().Be("158?EMS1.107");
    }
}
