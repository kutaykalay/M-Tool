using System.Text.Json;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device.Config;

/// <summary>The msi-ec WMI2 firmware list (plan asama15 §4.1-4.2): exact firmware, families and per-family mode bytes.</summary>
public class Wmi2FirmwareListTests
{
    private static readonly IReadOnlyList<DeviceConfig> Catalog = [P65()];

    private const string ValidJson = """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105", "15M3EMS1.113", "15M1IMS2.105", "14D3EMS1.116"],
          "families": ["15M3EMS1.1", "15M1IMS2.1", "14D3EMS1.1"],
          "familyOverrides": {
            "15M1IMS2.1": { "eco": "0xC6" },
            "14D3EMS1.1": { "high": "0xC0" }
          }
        }
        """;

    private static Wmi2FirmwareList Valid() => Wmi2FirmwareList.Parse(ValidJson);

    private static Wmi2FirmwareList With(
        IReadOnlyList<string>? exact = null,
        IReadOnlyList<string>? families = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<PerformanceMode, byte>>? overrides = null) =>
        Valid() with
        {
            Exact = exact ?? Valid().Exact,
            Families = families ?? Valid().Families,
            FamilyOverrides = overrides ?? Valid().FamilyOverrides,
        };

    private static Dictionary<string, IReadOnlyDictionary<PerformanceMode, byte>> Override(string family, PerformanceMode mode, byte value) =>
        new() { [family] = new Dictionary<PerformanceMode, byte> { [mode] = value } };

    private static FirmwareInfo Fw(string version) => new(version, "");

    private static void Rejects(Wmi2FirmwareList list, string pattern, IReadOnlyList<DeviceConfig>? catalog = null) =>
        Wmi2FirmwareList.Validate(list, catalog ?? Catalog).Should().Contain(e => e.Contains(pattern, StringComparison.Ordinal));

    private static string Json(string from, string to) => ValidJson.Replace(from, to, StringComparison.Ordinal);

    [Fact]
    public void A_valid_list_loads()
    {
        var list = Wmi2FirmwareList.Load(ValidJson, Catalog);

        list.Exact.Should().HaveCount(4);
        list.FamilyOverrides["15M1IMS2.1"][PerformanceMode.Eco].Should().Be(0xC6);
    }

    [Fact]
    public void Load_refuses_a_list_that_fails_validation()
    {
        var act = () => Wmi2FirmwareList.Load(Json("\"0xC6\"", "\"0xC5\""), Catalog);

        act.Should().Throw<InvalidDataException>().WithMessage("*0xC5*");
    }

    [Fact]
    public void Load_refuses_unreadable_json()
    {
        var act = () => Wmi2FirmwareList.Load("{", Catalog);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void A_list_without_overrides_is_valid()
    {
        Wmi2FirmwareList.Validate(With(overrides: new Dictionary<string, IReadOnlyDictionary<PerformanceMode, byte>>()), Catalog)
            .Should().BeEmpty();
    }

    [Fact]
    public void An_empty_catalog_is_fine_for_a_list_without_p65_firmware()
    {
        Wmi2FirmwareList.Validate(Valid(), []).Should().BeEmpty();
    }

    [Theory]
    [InlineData("15M3EMS1.105", MatchKind.Exact)]
    [InlineData("15M3EMS1.113", MatchKind.Exact)]
    [InlineData("15M3EMS1.199", MatchKind.Family)]
    [InlineData("15M3EMS1.1", MatchKind.Family)] // as long as the family: same as FirmwareMatcher
    [InlineData("16Q4EMS2.107", MatchKind.None)]
    [InlineData("15M3EMS2.105", MatchKind.None)]
    [InlineData("15M3EMS1", MatchKind.None)]
    [InlineData("", MatchKind.None)]
    public void Matches_exact_then_family(string firmware, MatchKind expected)
    {
        Valid().Match(Fw(firmware)).Should().Be(expected);
    }

    [Fact]
    public void An_unreadable_firmware_matches_nothing()
    {
        Valid().Match(null).Should().Be(MatchKind.None);
    }

    [Theory]
    [InlineData("15M1IMS2.105", PerformanceMode.Eco, 0xC6)]
    [InlineData("15M1IMS2.105", PerformanceMode.High, 0xC4)]
    [InlineData("15M1IMS2.105", PerformanceMode.Balanced, 0xC1)]
    [InlineData("14D3EMS1.116", PerformanceMode.High, 0xC0)]
    [InlineData("14D3EMS1.116", PerformanceMode.Eco, 0xC2)]
    [InlineData("15M3EMS1.105", PerformanceMode.Eco, 0xC2)]
    [InlineData("15M3EMS1.105", PerformanceMode.High, 0xC4)]
    public void An_exact_firmware_gets_the_family_override_or_the_standard_byte(string firmware, PerformanceMode mode, byte expected)
    {
        Valid().PerformanceByte(Fw(firmware), mode).Should().Be(expected);
    }

    [Theory]
    [InlineData("15M1IMS2.199")] // family only: read-only
    [InlineData("16Q4EMS2.107")]
    [InlineData("15M1")]
    [InlineData("")]
    public void Only_an_exact_firmware_gets_a_byte(string firmware)
    {
        Valid().PerformanceByte(Fw(firmware), PerformanceMode.Eco).Should().BeNull();
        Valid().PerformanceByte(null, PerformanceMode.Eco).Should().BeNull();
    }

    [Fact]
    public void An_undefined_mode_gets_no_byte()
    {
        var act = () => Valid().PerformanceByte(Fw("15M3EMS1.105"), (PerformanceMode)7);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_unvalidated_override_is_never_served()
    {
        var bad = With(overrides: Override("15M1IMS2.1", PerformanceMode.Eco, 0xC5));

        var act = () => bad.PerformanceByte(Fw("15M1IMS2.105"), PerformanceMode.Eco);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Rejects_an_unknown_schema_version(int version)
    {
        Rejects(Valid() with { SchemaVersion = version }, "schemaVersion");
    }

    [Fact]
    public void Rejects_a_list_without_sources()
    {
        Rejects(Valid() with { Sources = [] }, "sources");
    }

    [Fact]
    public void Rejects_an_empty_exact_list()
    {
        Rejects(With(exact: [], families: [], overrides: new Dictionary<string, IReadOnlyDictionary<PerformanceMode, byte>>()), "exact cannot be empty");
    }

    [Theory]
    [InlineData("15M3EMS1.10")]
    [InlineData("15M3EMS1.1050")]
    public void Rejects_firmware_that_is_not_12_characters(string firmware)
    {
        var errors = Wmi2FirmwareList.Validate(With(exact: [.. Valid().Exact, firmware]), Catalog);

        errors.Should().ContainSingle().Which.Should().Contain("12 characters");
    }

    [Fact]
    public void Rejects_firmware_with_a_control_character()
    {
        Rejects(With(exact: [.. Valid().Exact, "15M3EMS1.10\n"]), "control");
    }

    [Theory]
    [InlineData("15m3EMS1.106")] // lowercase
    [InlineData("15M3EMS1 106")] // space instead of the dot
    [InlineData("15M3EMS1.1 6")]
    [InlineData("15M3EMŞ1.106")] // non-ASCII
    [InlineData("15M3EMS1-106")]
    public void Rejects_firmware_outside_the_firmware_pattern(string firmware)
    {
        Rejects(With(exact: [.. Valid().Exact, firmware]), "pattern");
    }

    [Theory]
    [InlineData("15m3EMS1.1")]
    [InlineData("15M3EMS1-1")]
    [InlineData("15M3EMŞ1.1")]
    public void Rejects_a_family_outside_the_family_pattern(string family)
    {
        Rejects(With(families: [.. Valid().Families, family]), "pattern");
    }

    [Theory]
    [InlineData("158NIMS1.10D")] // hex-like build letters are real
    [InlineData("15FMIBA1.102")]
    public void Accepts_real_firmware_with_letters(string firmware)
    {
        var list = With(exact: [.. Valid().Exact, firmware], families: [.. Valid().Families, firmware[..10]]);

        Wmi2FirmwareList.Validate(list, Catalog).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_a_repeated_firmware()
    {
        Rejects(With(exact: [.. Valid().Exact, "15M3EMS1.105"]), "listed twice");
    }

    [Fact]
    public void Rejects_a_repeated_family()
    {
        Rejects(With(families: [.. Valid().Families, "15M3EMS1.1"]), "listed twice");
    }

    [Fact]
    public void Rejects_a_family_that_is_not_10_characters()
    {
        Rejects(With(families: [.. Valid().Families, "15M3EMS1."]), "10 characters");
    }

    [Fact]
    public void Rejects_firmware_outside_the_families()
    {
        Rejects(With(exact: [.. Valid().Exact, "1822EMS1.110"]), "not in the families");
    }

    [Fact]
    public void Rejects_a_family_without_firmware()
    {
        Rejects(With(families: [.. Valid().Families, "1822EMS1.1"]), "no exact firmware");
    }

    [Fact]
    public void Rejects_firmware_a_record_lists()
    {
        var wmi1 = Draft() with { Id = "wmi1", Firmware = new FirmwareSpec(["15M3EMS1.105"], ["15M3EMS1.1"]) };

        Rejects(Valid(), "firmware \"15M3EMS1.105\" is listed by a device record", [wmi1]);
    }

    [Fact]
    public void Rejects_firmware_inside_a_records_family()
    {
        var wmi1 = Draft() with { Id = "wmi1", Firmware = new FirmwareSpec([], ["15M3EMS1.1"]) };

        Rejects(Valid(), "firmware \"15M3EMS1.105\" is in a device record's family", [wmi1]);
    }

    [Fact]
    public void Rejects_a_family_a_record_claims()
    {
        var wmi1 = Draft() with { Id = "wmi1", Firmware = new FirmwareSpec([], ["15M1IMS2.1"]) };

        Rejects(Valid(), "family \"15M1IMS2.1\" is a device record's family", [wmi1]);
    }

    [Fact]
    public void Rejects_a_family_that_holds_a_records_firmware()
    {
        var wmi1 = Draft() with { Id = "wmi1", Firmware = new FirmwareSpec(["14D3EMS1.199"], []) };

        Rejects(Valid(), "family \"14D3EMS1.1\" holds device record firmware \"14D3EMS1.199\"", [wmi1]);
    }

    [Fact]
    public void The_p65_firmware_stays_out_even_when_its_record_did_not_load()
    {
        // The code's write-verified list is a key of its own: a skipped P65 record must not free its firmware.
        var withP65 = With(exact: [.. Valid().Exact, "16Q4EMS2.107"], families: [.. Valid().Families, "16Q4EMS2.1"]);

        Rejects(withP65, "firmware \"16Q4EMS2.107\" is listed by a device record", []);
        Rejects(withP65, "family \"16Q4EMS2.1\" is a device record's family", []);
    }

    [Fact]
    public void Rejects_the_p65_family_without_the_p65_firmware()
    {
        Rejects(With(exact: [.. Valid().Exact, "16Q4EMS2.108"], families: [.. Valid().Families, "16Q4EMS2.1"]), "16Q4EMS2.1", []);
    }

    [Fact]
    public void Rejects_an_override_for_a_family_not_in_the_list()
    {
        Rejects(With(overrides: Override("1822EMS1.1", PerformanceMode.Eco, 0xC6)), "1822EMS1.1");
    }

    [Theory]
    [InlineData(PerformanceMode.Eco, 0xC5)] // MSI Center's fourth mode: never written
    [InlineData(PerformanceMode.Eco, 0xC2)] // a standard byte is not an override
    [InlineData(PerformanceMode.Eco, 0xC0)] // the high exception: no swapped meanings
    [InlineData(PerformanceMode.High, 0xC6)]
    [InlineData(PerformanceMode.Balanced, 0xC6)]
    [InlineData(PerformanceMode.Eco, 0x00)]
    [InlineData((PerformanceMode)7, 0xC6)]
    public void Rejects_an_override_the_code_does_not_allow_for_that_mode(PerformanceMode mode, byte value)
    {
        Rejects(With(overrides: Override("15M1IMS2.1", mode, value)), $"0x{value:X2}");
    }

    [Fact]
    public void Rejects_an_override_without_modes()
    {
        var empty = new Dictionary<string, IReadOnlyDictionary<PerformanceMode, byte>>
        {
            ["15M1IMS2.1"] = new Dictionary<PerformanceMode, byte>(),
        };

        Rejects(With(overrides: empty), "no mode");
    }

    [Theory]
    [InlineData("\"exact\": [\"15M3EMS1.105\",", "\"exact\": [null, \"15M3EMS1.105\",")]
    [InlineData("\"sources\": [\"msi-ec d7fbbd8\"]", "\"sources\": [null]")]
    [InlineData("\"families\": [\"15M3EMS1.1\",", "\"families\": [null, \"15M3EMS1.1\",")]
    [InlineData("{ \"eco\": \"0xC6\" }", "null")]
    public void A_null_item_is_refused(string from, string to)
    {
        var act = () => Wmi2FirmwareList.Load(Json(from, to), Catalog);

        act.Should().Throw<Exception>().Where(e => e is JsonException || e is InvalidDataException);
    }

    [Theory]
    [InlineData("\"exact\"", "\"exactly\"")] // unknown root field
    [InlineData("\"eco\"", "\"superBattery\"")] // unknown mode
    [InlineData("\"eco\"", "\"2\"")] // a mode as a number
    [InlineData("\"0xC6\"", "198")] // a byte as a number
    [InlineData("\"0xC6\"", "\"C6\"")] // a byte without 0x
    [InlineData("\"0xC6\"", "\"0x1C6\"")] // above 0xFF
    [InlineData("\"schemaVersion\": 1,", "")] // missing value field
    [InlineData("\"families\": [\"15M3EMS1.1\", \"15M1IMS2.1\", \"14D3EMS1.1\"],", "")] // missing list
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,")] // repeated field
    [InlineData("{ \"eco\": \"0xC6\" }", "{ \"eco\": \"0xC6\", \"eco\": \"0xC6\" }")] // repeated mode
    [InlineData("\"14D3EMS1.1\": { \"high\": \"0xC0\" }", "\"14D3EMS1.1\": { \"high\": \"0xC0\" }, \"14D3EMS1.1\": { \"high\": \"0xC0\" }")] // repeated family
    public void Parse_rejects_anything_but_the_exact_shape(string from, string to)
    {
        var act = () => Wmi2FirmwareList.Parse(Json(from, to));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Parse_rejects_null()
    {
        var act = () => Wmi2FirmwareList.Parse("null");

        act.Should().Throw<JsonException>();
    }
}
