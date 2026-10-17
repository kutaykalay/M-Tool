using System.Globalization;
using System.Text;
using System.Text.Json;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Tests.Fakes;

namespace MTool.Tests.Diagnostics;

public sealed class EcSubmissionTests
{
    private static readonly SystemInfo P65System = new(
        "Micro-Star International Co., Ltd.", "P65 Creator 9SE", "MS-16Q4", "E16Q4IMS.10D", "2019-05-13",
        "Intel(R) Core(TM) i7-9750H CPU @ 2.60GHz", "Windows 11 Home 10.0.26300");

    private static readonly IReadOnlyList<WmiClassInfo> P65Classes =
        [new WmiClassInfo("MSI_CPU", ["Active", "CPU", "InstanceName"]), new WmiClassInfo("MSI_System", [])];

    private static EcReadout P65Ec(params FieldReadout[] fields) => new(
        new FirmwareInfo("16Q4EMS2.107", "05132019"),
        new DeviceMatch(MatchKind.Exact, "msi-p65-creator-9se", "MSI P65 Creator 9SE"),
        "msi-p65-creator-9se",
        TestLayouts.P65.Capabilities,
        new FieldScan(fields, 0));

    private static DeviceReportInput Input(
        Section<SystemInfo>? system = null,
        Section<WmiInterface?>? wmi = null,
        Section<EcReadout>? ec = null,
        Section<Wmi2Readout>? wmi2 = null,
        Section<IReadOnlyList<WmiClassInfo>>? classes = null) => new(
            "0.12.0-beta.1+abc1234",
            new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.FromHours(3)),
            system ?? Section.Ok(P65System),
            wmi ?? Section.Ok<WmiInterface?>(WmiInterface.Wmi1),
            classes ?? Section.Ok(P65Classes),
            Section.Ok(240_000),
            ec ?? Section.Ok(P65Ec(new FieldReadout(0x68, "MSI_CPU[1]", 0x2D), new FieldReadout(0x71, "MSI_CPU[3]", null))),
            wmi2);

    private static string Json(DeviceReportInput input, params string[] privateWords) => EcSubmission.ToJson(input, privateWords);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void The_payload_is_byte_for_byte_the_contract_sample()
    {
        var sample = File.ReadAllText(Path.Combine(RepoFiles.Root, "tests", "MTool.Tests", "Contracts", "ec-submission-v1.sample.json"));

        Json(Input()).Should().Be(sample.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    /// <summary>
    /// A WMI2 laptop's payload with every report call (the M-Tool-reports Worker tests this same file):
    /// a call it does not know would be refused with 400. One call failed, so a null packet is in it too.
    /// </summary>
    [Fact]
    public void The_wmi2_payload_is_byte_for_byte_the_wmi2_contract_sample()
    {
        var sample = File.ReadAllText(Path.Combine(RepoFiles.Root, "tests", "MTool.Tests", "Contracts", "ec-submission-v1-wmi2.sample.json"));

        Json(Wmi2Input()).Should().Be(sample.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    private static DeviceReportInput Wmi2Input()
    {
        var calls = Wmi2Probe.Calls.Select((call, i) =>
        {
            if (call is ("Get_Thermal", 2))
            {
                return new Wmi2Call(call.Method, call.Sub, null, "TimeoutException: no answer");
            }

            var packet = new byte[Wmi2Probe.PacketLength];
            (packet[0], packet[1], packet[2]) = (Wmi2Probe.Success, call.Sub, (byte)i);
            return new Wmi2Call(call.Method, call.Sub, packet, null);
        });
        return Input(
            system: Section.Ok(P65System with { Model = "Katana GF66 11UE", BaseBoard = "MS-1581" }),
            wmi: Section.Ok<WmiInterface?>(WmiInterface.Wmi2),
            classes: Section.Ok<IReadOnlyList<WmiClassInfo>>([new WmiClassInfo("MSI_ACPI", [])]),
            ec: Section.Failed<EcReadout>("not read: M-Tool reads the EC only through WMI1 so far"),
            wmi2: Section.Ok(new Wmi2Readout(calls.ToList())));
    }

    [Fact]
    public void Lines_end_with_a_line_feed_only() =>
        Json(Input()).Should().Contain("\n").And.NotContain("\r");

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void The_payload_does_not_depend_on_the_culture(string culture)
    {
        var expected = Json(Input());
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Json(Input()).Should().Be(expected);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    [Fact]
    public void A_failed_part_is_a_fixed_code_and_its_message_stays_out()
    {
        var input = Input(
            system: Section.Failed<SystemInfo>(@"IOException: C:\Users\Kutay\x is locked"),
            ec: Section.Failed<EcReadout>(@"InvalidOperationException: C:\Users\Kutay\AppData failed"));

        var json = Json(input);

        json.Should().NotContain("Kutay").And.NotContain("Exception").And.NotContain("locked");
        var root = Parse(json);
        root.GetProperty("ec").GetProperty("status").GetString().Should().Be("failed");
        root.GetProperty("system").EnumerateObject().Should().HaveCount(7).And.OnlyContain(p => p.Value.ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData(WmiInterface.Wmi2, "WMI2")]
    [InlineData(null, "none")]
    public void Without_WMI1_the_EC_is_not_read(WmiInterface? wmi, string name)
    {
        var root = Parse(Json(Input(wmi: Section.Ok(wmi), ec: Section.Failed<EcReadout>("not read: no WMI1"))));

        root.GetProperty("wmi").GetProperty("interface").GetString().Should().Be(name);
        var ec = root.GetProperty("ec");
        ec.GetProperty("status").GetString().Should().Be("not_read");
        ec.GetProperty("firmware").ValueKind.Should().Be(JsonValueKind.Null);
        ec.GetProperty("match").GetProperty("kind").GetString().Should().Be("None");
        ec.GetProperty("layoutId").ValueKind.Should().Be(JsonValueKind.Null);
        ec.GetProperty("fields").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void An_interface_that_could_not_be_probed_is_none_and_the_EC_not_read()
    {
        var root = Parse(Json(Input(wmi: Section.Failed<WmiInterface?>("timeout"), ec: Section.Failed<EcReadout>("not read"))));

        root.GetProperty("wmi").GetProperty("interface").GetString().Should().Be("none");
        root.GetProperty("ec").GetProperty("status").GetString().Should().Be("not_read");
    }

    [Fact]
    public void Classes_that_could_not_be_read_are_an_empty_list() =>
        Parse(Json(Input(classes: Section.Failed<IReadOnlyList<WmiClassInfo>>("access denied"))))
            .GetProperty("wmi").GetProperty("classes").GetArrayLength().Should().Be(0);

    [Fact]
    public void An_unread_firmware_is_null()
    {
        var ec = new EcReadout(null, DeviceMatch.None, "wmi1-generic", TestLayouts.P65.Capabilities, new FieldScan([], 5));

        var root = Parse(Json(Input(ec: Section.Ok(ec)))).GetProperty("ec");

        root.GetProperty("firmware").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("firmwareDate").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("match").GetProperty("recordId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("skipped").GetInt32().Should().Be(5);
    }

    [Fact]
    public void A_private_word_is_hidden_and_a_look_alike_hides_the_whole_value()
    {
        var system = P65System with { Model = "Kutay P65", BaseBoard = "ｋｕｔａｙ board" };

        var json = Json(Input(system: Section.Ok(system)), "Kutay");

        var s = Parse(json).GetProperty("system");
        s.GetProperty("model").GetString().Should().Be("<hidden> P65");
        s.GetProperty("baseBoard").GetString().Should().Be("<hidden>");
        json.Should().NotContainEquivalentOf("kutay").And.NotContain("ｋｕｔａｙ");
    }

    [Theory]
    [InlineData("Ku​tay P65")]
    [InlineData("DESKTOP-KUTAY")]
    [InlineData("KUTAYPC")]
    public void A_name_split_by_a_hidden_character_or_joined_to_another_word_hides_the_whole_value(string model)
    {
        var json = Json(Input(system: Section.Ok(P65System with { Model = model })), "Kutay");

        Parse(json).GetProperty("system").GetProperty("model").GetString().Should().Be("<hidden>");
    }

    [Fact]
    public void Every_text_value_is_masked_not_only_the_system()
    {
        var ec = new EcReadout(new FirmwareInfo("Kutay1", "Kutay"), new DeviceMatch(MatchKind.Family, "kutay-record", null), "kutay-layout",
            TestLayouts.P65.Capabilities, new FieldScan([new FieldReadout(0x68, "Kutay[1]", 1)], 0));
        var wmi2 = new Wmi2Readout([new Wmi2Call("Get_Kutay", 0, null, null)]);
        var input = Input(ec: Section.Ok(ec), wmi2: Section.Ok(wmi2),
            classes: Section.Ok<IReadOnlyList<WmiClassInfo>>([new WmiClassInfo("Kutay_Class", ["Kutay"])])) with
        { AppVersion = "1.0 Kutay" };

        Json(input, "Kutay").Should().NotContainEquivalentOf("kutay");
    }

    [Fact]
    public void Hidden_and_non_ASCII_text_is_written_as_is_not_escaped()
    {
        var system = P65System with { Model = "Kutay <P65> + İş" };

        Json(Input(system: Section.Ok(system)), "Kutay").Should().Contain("\"model\": \"<hidden> <P65> + İş\"");
    }

    [Fact]
    public void Characters_that_are_not_shown_are_dropped_and_white_space_is_one_space()
    {
        var system = P65System with { Model = "  P65\u202E\u200B  Creator\t\n9SE\uE000 ", Cpu = "\u200B" };

        var s = Parse(Json(Input(system: Section.Ok(system)))).GetProperty("system");

        s.GetProperty("model").GetString().Should().Be("P65 Creator 9SE");
        s.GetProperty("cpu").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void A_lone_surrogate_is_dropped_not_written()
    {
        var system = P65System with { Model = "P65\uD800 Creator" };

        Parse(Json(Input(system: Section.Ok(system)))).GetProperty("system").GetProperty("model").GetString().Should().Be("P65 Creator");
    }

    [Fact]
    public void Text_is_cut_to_the_limit_without_splitting_a_character()
    {
        var system = P65System with { Model = new string('a', EcSubmission.MaxTextLength - 1) + "😀tail" };

        var model = Parse(Json(Input(system: Section.Ok(system)))).GetProperty("system").GetProperty("model").GetString()!;

        model.Should().Be(new string('a', EcSubmission.MaxTextLength - 1));
    }

    [Fact]
    public void WMI2_not_asked_for_is_not_asked_with_no_calls()
    {
        var wmi2 = Parse(Json(Input())).GetProperty("wmi2");

        wmi2.GetProperty("status").GetString().Should().Be("not_asked");
        wmi2.GetProperty("calls").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void WMI2_that_failed_is_failed_with_no_calls()
    {
        var wmi2 = Parse(Json(Input(wmi2: Section.Failed<Wmi2Readout>("no answer")))).GetProperty("wmi2");

        wmi2.GetProperty("status").GetString().Should().Be("failed");
        wmi2.GetProperty("calls").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void WMI2_packets_are_upper_case_hex_and_a_missing_or_short_one_is_null()
    {
        var full = Enumerable.Range(0, Wmi2Probe.PacketLength).Select(i => (byte)(i * 7)).ToArray();
        var readout = new Wmi2Readout([
            new Wmi2Call("Get_EC", 0, full, null),
            new Wmi2Call("Get_Fan", 1, null, "timeout"),
            new Wmi2Call("Get_AP", 0, new byte[5], null),
        ]);

        var calls = Parse(Json(Input(wmi2: Section.Ok(readout)))).GetProperty("wmi2");

        calls.GetProperty("status").GetString().Should().Be("ok");
        var list = calls.GetProperty("calls").EnumerateArray().ToList();
        list.Should().HaveCount(3);
        list[0].GetProperty("method").GetString().Should().Be("Get_EC");
        list[0].GetProperty("sub").GetInt32().Should().Be(0);
        list[0].GetProperty("packet").GetString().Should().Be(string.Join(' ', full.Select(b => b.ToString("X2", CultureInfo.InvariantCulture))));
        list[1].GetProperty("packet").ValueKind.Should().Be(JsonValueKind.Null);
        list[2].GetProperty("packet").ValueKind.Should().Be(JsonValueKind.Null);
        calls.ToString().Should().NotContain("timeout");
    }

    [Fact]
    public void No_key_names_anything_that_identifies_a_person_or_a_moment()
    {
        string[] contains = ["serial", "uuid", "user", "machine", "host", "path", "email", "time", "date"];
        string[] allowedDates = ["biosDate", "firmwareDate"];
        var full = Enumerable.Repeat((byte)1, Wmi2Probe.PacketLength).ToArray();
        var keys = new List<string>();
        Collect(Parse(Json(Input(wmi2: Section.Ok(new Wmi2Readout([new Wmi2Call("Get_EC", 0, full, null)]))))), keys);

        keys.Except(allowedDates).Should().NotContain(k => contains.Any(c => k.Contains(c, StringComparison.OrdinalIgnoreCase)) || k == "ip");
        keys.Should().Contain(["model", "register", "packet"]); // the walk really went through the document
    }

    [Fact]
    public void The_largest_realistic_payload_fits_the_limit()
    {
        var longText = new string('W', EcSubmission.MaxTextLength);
        var system = new SystemInfo(longText, longText, longText, longText, longText, longText, longText);
        // Over twice the P65 (5 classes, 56 fields); a larger body is refused before sending, not cut.
        var classes = Enumerable.Range(0, 10).Select(i => new WmiClassInfo($"MSI_Class{i}", Enumerable.Range(0, 10).Select(p => $"Property{p}").ToList())).ToList();
        var fields = Enumerable.Range(0, 128).Select(r => new FieldReadout((byte)r, $"MSI_Something[{r}]", (byte)r)).ToArray();
        var packet = Enumerable.Repeat((byte)0xAB, Wmi2Probe.PacketLength).ToArray();
        var wmi2 = new Wmi2Readout(Wmi2Probe.Calls.Select(c => new Wmi2Call(c.Method, c.Sub, packet, null)).ToList());

        var json = Json(Input(system: Section.Ok(system), classes: Section.Ok<IReadOnlyList<WmiClassInfo>>(classes),
            ec: Section.Ok(P65Ec(fields)), wmi2: Section.Ok(wmi2)));

        Encoding.UTF8.GetByteCount(json).Should().BeLessThan(EcSubmission.MaxBytes);
    }

    private static void Collect(JsonElement element, List<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    keys.Add(property.Name);
                    Collect(property.Value, keys);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, keys);
                }

                break;
        }
    }
}
