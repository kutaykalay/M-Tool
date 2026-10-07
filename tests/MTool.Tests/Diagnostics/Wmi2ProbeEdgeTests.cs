using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

/// <summary>Review findings on the WMI2 probe (7g): hung methods, raw bytes, byte order, per-field failures.</summary>
public class Wmi2ProbeEdgeTests
{
    private static byte[] Ok(params (int Index, byte Value)[] values)
    {
        var packet = new byte[Wmi2Probe.PacketLength];
        packet[0] = Wmi2Probe.Success;
        foreach (var (index, value) in values)
        {
            packet[index] = value;
        }

        return packet;
    }

    [Fact]
    public void Stops_after_two_failures_in_a_row_and_names_the_skipped_calls()
    {
        var made = 0;

        var readout = Wmi2Probe.Run((method, _) =>
        {
            made++;
            return method == "Get_EC" ? Ok() : throw new TimeoutException("zaman aşımı");
        });

        made.Should().Be(4, "Get_WMI fails, Get_EC answers, two Get_Temperature calls fail, then it stops");
        readout.Calls.Should().HaveCount(Wmi2Probe.Calls.Count);
        readout.Calls.Skip(4).Should().OnlyContain(c => c.Packet == null && c.Error!.Contains("skipped"));
    }

    [Fact]
    public void A_single_failure_between_answers_does_not_stop_the_probe()
    {
        var made = 0;

        Wmi2Probe.Run((method, sub) =>
        {
            made++;
            return method == "Get_EC" ? throw new InvalidOperationException("yok") : Ok();
        });

        made.Should().Be(Wmi2Probe.Calls.Count);
    }

    [Fact]
    public void Packets_are_kept_byte_for_byte_and_copied()
    {
        var source = Ok((5, 0xAB), (31, 0xCD));

        var readout = Wmi2Probe.Run((_, _) => source);
        source[5] = 0x00;

        readout.Calls[0].Packet.Should().Equal(Ok((5, 0xAB), (31, 0xCD)));
    }

    [Fact]
    public void A_call_record_keeps_its_own_copy()
    {
        var bytes = Ok((3, 7));
        var call = new Wmi2Call("Get_AP", 0, bytes, null);

        bytes[3] = 9;

        call.Packet![3].Should().Be(7);
    }

    [Fact]
    public void The_fan_period_is_high_byte_first()
    {
        var reading = Wmi2Probe.Run((method, sub) => (method, sub) == ("Get_Fan", 0) ? Ok((1, 0x01), (2, 0x00), (3, 0x00), (4, 0xF0)) : Ok())
            .Interpret();

        reading.CpuRpm.Should().Be(478_000 / 0x0100);
        reading.GpuRpm.Should().Be(478_000 / 0x00F0);
    }

    [Fact]
    public void A_failed_fan_packet_leaves_the_temperatures_alone()
    {
        var reading = Wmi2Probe.Run((method, sub) => (method, sub) switch
        {
            ("Get_Fan", 0) => new byte[Wmi2Probe.PacketLength], // first byte 0: the method failed
            ("Get_Temperature", 0) => Ok((1, 55), (2, 44)),
            _ => Ok(),
        }).Interpret();

        reading.CpuRpm.Should().BeNull();
        reading.GpuRpm.Should().BeNull();
        reading.CpuTempC.Should().Be(55);
    }
}
