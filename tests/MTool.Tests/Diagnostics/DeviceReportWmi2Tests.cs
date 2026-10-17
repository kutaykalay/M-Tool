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
            .Should().Contain("not read: the report was made without WMI2; use the WMI2 report item in the tray menu or run M-Tool.exe --report --wmi2.");
    }

    [Fact]
    public void A_wmi1_laptop_has_no_wmi2_section()
    {
        DeviceReport.Format(Input(WmiInterface.Wmi1, wmi2: null), privateWords: []).Should().NotContain("WMI2, Get_*");
    }

    /// <summary>The value each Get_Data register answers: temperatures 60/46, fans 45/30 %, CPU tach one byte, GPU tach unclear.</summary>
    private static readonly Dictionary<byte, byte> DataAnswers = new()
    {
        [0x68] = 0x3C,
        [0x80] = 0x2E,
        [0x71] = 0x2D,
        [0x89] = 0x1E,
        [0xC8] = 0x00,
        [0xC9] = 0xA0,
        [0xCA] = 0x01,
        [0xCB] = 0xA0,
        [0x98] = 0x00,
        [0xD2] = 0xC1,
        [0xD4] = 0x0D,
        [0xD7] = 0xD0,
    };

    private static byte[] DataPacket(byte value, byte flag = Wmi2Probe.Success)
    {
        var packet = new byte[Wmi2Probe.PacketLength];
        (packet[0], packet[1]) = (flag, value);
        return packet;
    }

    private static Wmi2Readout WithData(Func<byte, Wmi2Call?>? replace = null) => new(
        [.. Readout().Calls, .. DataAnswers.Select(a => replace?.Invoke(a.Key) ?? new Wmi2Call("Get_Data", a.Key, DataPacket(a.Value), null))]);

    private static string Report(Wmi2Readout readout) =>
        DeviceReport.Format(Input(WmiInterface.Wmi2, Section.Ok(readout)), privateWords: []);

    [Fact]
    public void Get_data_names_its_register_in_hex()
    {
        Report(WithData()).Should().Contain("  Get_Data(0x68) : 01 3C 00 00");
    }

    [Fact]
    public void The_get_data_reading_sits_next_to_the_packet_reading_with_its_raw_bytes()
    {
        var text = Report(WithData());

        text.Should().Contain("Get_Data     : the pinned EC registers, unverified")
            .And.Contain("Temperatures : CPU 60 C (0x68 = 3C), GPU 46 C (0x80 = 2E)")
            .And.Contain("Fans         : CPU 45 % (0x71 = 2D), 2987 RPM (0xC8:0xC9 = 00 A0); GPU 30 % (0x89 = 1E), RPM unclear (0xCA:0xCB = 01 A0)")
            .And.Contain("Status       : 0x98 = 00, 0xD2 = C1, 0xD4 = 0D, 0xD7 = D0");
        text.IndexOf("Get_Data     :", StringComparison.Ordinal).Should().BeGreaterThan(text.IndexOf("Reading      :", StringComparison.Ordinal));
    }

    [Fact]
    public void An_implausible_temperature_is_a_question_mark_with_its_raw_byte()
    {
        var text = Report(WithData(r => r == 0x80 ? new Wmi2Call("Get_Data", r, DataPacket(0x00), null) : null));

        text.Should().Contain("GPU ? C (0x80 = 00)");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("flag")]
    public void A_register_that_did_not_answer_leaves_the_reading_out_and_says_how_many_did(string how)
    {
        var text = Report(WithData(r => r != 0xD4 ? null
            : how == "failed" ? new Wmi2Call("Get_Data", r, null, "TimeoutException: no answer")
            : new Wmi2Call("Get_Data", r, DataPacket(0x0D, flag: 0), null)));

        text.Should().Contain("Get_Data     : incomplete, 11 of 12 registers answered")
            .And.NotContain("Status       :");
    }

    [Fact]
    public void A_reading_that_throws_keeps_the_raw_packets_and_names_the_failure()
    {
        var readout = new Wmi2Readout(new ReadableOnce([new Wmi2Call("Get_Temperature", 0, [1, 0], null)]));

        var text = DeviceReport.Format(Input(WmiInterface.Wmi2, Section.Ok(readout)), privateWords: []);

        text.Should().Contain("  Get_Temperature(0) : 01 00")
            .And.Contain("Reading      : unreadable (InvalidOperationException)")
            .And.NotContain("Temperatures :");
    }

    [Fact]
    public void A_readout_without_get_data_has_no_get_data_reading()
    {
        Report(Readout()).Should().NotContain("Get_Data");
    }

    /// <summary>
    /// Lists its calls once (so the raw lines are written), then throws: the reading of today's packets
    /// cannot fail, so this stands in for whatever might go wrong with a later guess.
    /// </summary>
    private sealed class ReadableOnce(IReadOnlyList<Wmi2Call> calls) : IReadOnlyList<Wmi2Call>
    {
        private int _reads;

        public int Count => calls.Count;

        public Wmi2Call this[int index] => calls[index];

        public IEnumerator<Wmi2Call> GetEnumerator() => _reads++ == 0 ? calls.GetEnumerator() : throw new InvalidOperationException("read twice");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
