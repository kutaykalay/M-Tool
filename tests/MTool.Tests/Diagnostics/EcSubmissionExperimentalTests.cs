using System.Text.Json;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Diagnostics;

/// <summary>
/// The <c>experimental</c> block (plan asama15 15a-2, filled in 15c-6): only after a WMI2 experimental write in this
/// session, with the answers the user gave in the preview. The M-Tool-reports Worker tests the same contract file.
/// </summary>
public sealed class EcSubmissionExperimentalTests
{
    private const string Firmware = "15M3EMS1.109";

    private static DeviceReportInput Input(ExperimentalSession? experimental) => new(
        "0.18.0-beta.1+abc1234",
        new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.FromHours(3)),
        Section.Ok(new SystemInfo(
            "Micro-Star International Co., Ltd.", "Katana GF66 11UE", "MS-1581", "E1581IMS.10D", "2021-05-13",
            "11th Gen Intel(R) Core(TM) i7-11800H @ 2.30GHz", "Windows 11 Home 10.0.26300")),
        WmiProbeOutcome.Answered(WmiInterface.Wmi2),
        Section.Ok<IReadOnlyList<WmiClassInfo>>([new WmiClassInfo("MSI_ACPI", [])]),
        new MsiInfo(new MsiSchemaInfo(MofImagePathState.NotSet, SchemaFileState.Missing, SchemaSignature.Unknown), new MsiSoftwareInfo(false, false)),
        Section.Ok(0),
        Section.Failed<EcReadout>("not read: M-Tool reads the EC only through WMI1 so far"),
        Experimental: experimental);

    private static ExperimentalSession Session(params Wmi2FeatureFeedback[] feedback) => new(
        Firmware,
        new Wmi2Status(0x02, 0xC1, 0x0D, 0xE4),
        LayoutVerified: true,
        [
            new Wmi2WriteRecord(Wmi2Feature.PerformanceMode, 0xC1, 0xC1, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.CoolerBoost, 0x02, 0x82, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.PerformanceMode, 0xC1, 0xC4, Wmi2ReadBack.Differs),
            new Wmi2WriteRecord(Wmi2Feature.ChargeLimit, 0xE4, 0xD0, Wmi2ReadBack.Failed),
        ],
        feedback);

    private static readonly Wmi2FeatureFeedback[] SampleAnswers =
    [
        new(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.Worked),
        new(Wmi2Feature.PerformanceMode, Wmi2FeatureAnswer.NotWorked),
        new(Wmi2Feature.ChargeLimit, Wmi2FeatureAnswer.NotTried),
    ];

    private static string Json(ExperimentalSession? session) => EcSubmission.ToJson(Input(session), []);

    private static JsonElement Block(string json) => JsonDocument.Parse(json).RootElement.GetProperty("experimental");

    [Fact]
    public void The_payload_is_byte_for_byte_the_experimental_contract_sample()
    {
        var sample = File.ReadAllText(Path.Combine(RepoFiles.Root, "tests", "MTool.Tests", "Contracts", "ec-submission-v2-experimental.sample.json"));

        Json(Session(SampleAnswers)).Should().Be(sample.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    [Fact]
    public void Without_a_session_there_is_no_block() =>
        JsonDocument.Parse(Json(null)).RootElement.TryGetProperty("experimental", out _).Should().BeFalse();

    [Fact]
    public void The_block_comes_last_and_has_exactly_the_contract_keys()
    {
        var root = JsonDocument.Parse(Json(Session(SampleAnswers))).RootElement;
        var block = root.GetProperty("experimental");

        root.EnumerateObject().Last().Name.Should().Be("experimental");
        block.EnumerateObject().Select(p => p.Name).Should().Equal("firmware", "match", "layoutCheck", "writes", "feedback");
        block.GetProperty("layoutCheck").GetProperty("bytes").EnumerateObject().Select(p => p.Name).Should().Equal("0xD2", "0xD4", "0xD7", "0x98");
        block.GetProperty("writes")[0].EnumerateObject().Select(p => p.Name).Should().Equal("feature", "before", "after", "readBack");
        block.GetProperty("feedback")[0].EnumerateObject().Select(p => p.Name).Should().Equal("feature", "user");
    }

    [Fact]
    public void Writes_only_happen_on_an_exact_match_so_match_is_exact()
    {
        Block(Json(Session())).GetProperty("match").GetString().Should().Be("exact");
    }

    [Fact]
    public void A_layout_that_did_not_fit_is_failed_with_its_raw_bytes()
    {
        var session = Session() with { LayoutStatus = new Wmi2Status(0x02, 0x00, 0x0D, 0xE4), LayoutVerified = false };

        var layout = Block(Json(session)).GetProperty("layoutCheck");

        layout.GetProperty("status").GetString().Should().Be("failed");
        layout.GetProperty("bytes").GetProperty("0xD2").GetString().Should().Be("0x00");
    }

    [Fact]
    public void No_answer_is_an_empty_feedback_list() =>
        Block(Json(Session())).GetProperty("feedback").GetArrayLength().Should().Be(0);

    /// <summary>The server takes exactly 12 characters; anything else would refuse the whole submission.</summary>
    [Theory]
    [InlineData("15M3EMS1.10")]
    [InlineData("15M3EMS1.1090")]
    [InlineData("")]
    public void A_firmware_that_is_not_twelve_characters_leaves_the_block_out(string firmware) =>
        JsonDocument.Parse(Json(Session() with { Firmware = firmware })).RootElement.TryGetProperty("experimental", out _).Should().BeFalse();

    [Fact]
    public void A_firmware_holding_a_private_name_leaves_the_block_out()
    {
        var json = EcSubmission.ToJson(Input(Session() with { Firmware = "KUTAY-PC.109" }), ["KUTAY-PC"]);

        JsonDocument.Parse(json).RootElement.TryGetProperty("experimental", out _).Should().BeFalse();
    }

    /// <summary>
    /// The same key ban as the plain payload (<see cref="EcSubmissionTests"/>), walked through the block. The one
    /// exception is the contract's <c>feedback[].user</c>, whose value is only worked / not_worked / not_tried.
    /// </summary>
    [Fact]
    public void No_key_in_the_block_names_a_person_or_a_moment_except_the_answer_key()
    {
        string[] banned = ["serial", "uuid", "user", "machine", "host", "path", "email", "time", "date", "ip"];
        var keys = new List<string>();
        Collect(Block(Json(Session(SampleAnswers))), keys);

        keys.Where(k => k != "user").Should().NotContain(k => banned.Any(b => k.Contains(b, StringComparison.OrdinalIgnoreCase)));
        Block(Json(Session(SampleAnswers))).GetProperty("feedback").EnumerateArray()
            .Select(f => f.GetProperty("user").GetString()).Should().OnlyContain(v => v == "worked" || v == "not_worked" || v == "not_tried");
    }

    private static void Collect(JsonElement element, List<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                keys.Add(property.Name);
                Collect(property.Value, keys);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, keys);
            }
        }
    }

    [Fact]
    public void Twenty_writes_and_four_answers_fit_the_size_limit()
    {
        var writes = Enumerable.Repeat(new Wmi2WriteRecord(Wmi2Feature.ChargeLimit, 0xE4, 0xD0, Wmi2ReadBack.Differs), Wmi2WriteJournal.MaxWrites).ToList();
        var answers = Enum.GetValues<Wmi2Feature>().Select(f => new Wmi2FeatureFeedback(f, Wmi2FeatureAnswer.NotWorked)).ToList();

        var json = Json(Session() with { Writes = writes, Feedback = answers });

        System.Text.Encoding.UTF8.GetByteCount(json).Should().BeLessThan(EcSubmission.MaxBytes);
    }
}
