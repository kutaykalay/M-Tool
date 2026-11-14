using System.Text.Json;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

/// <summary>
/// What "Send EC data" previews: the payload rendered again whenever the user answers a question, so the text
/// sent is always a text the preview showed (plan asama15 15c-6).
/// </summary>
public sealed class SubmissionDraftTests
{
    private static DeviceReportInput Input(ExperimentalSession? experimental = null) => new(
        "0.18.0-beta.1",
        new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero),
        Section.Ok(new SystemInfo("Micro-Star International Co., Ltd.", "Katana GF66 11UE", null, null, null, null, null)),
        WmiProbeOutcome.Answered(WmiInterface.Wmi2),
        Section.Ok<IReadOnlyList<WmiClassInfo>>([]),
        MsiInfo.Unknown,
        Section.Ok(0),
        Section.Failed<EcReadout>("not read"),
        Experimental: experimental);

    private static ExperimentalSession Session() => new(
        "15M3EMS1.109",
        new Wmi2Status(0x02, 0xC1, 0x0D, 0xE4),
        LayoutVerified: true,
        [
            new Wmi2WriteRecord(Wmi2Feature.ChargeLimit, 0xE4, 0xD0, Wmi2ReadBack.Ok),
            new Wmi2WriteRecord(Wmi2Feature.CoolerBoost, 0x02, 0x82, Wmi2ReadBack.Ok),
        ],
        []);

    private static IEnumerable<(string Feature, string User)> Feedback(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("experimental").GetProperty("feedback").EnumerateArray()
            .Select(f => (f.GetProperty("feature").GetString()!, f.GetProperty("user").GetString()!)).ToList();

    [Fact]
    public void Without_experimental_writes_there_is_nothing_to_ask_and_the_text_is_the_plain_payload()
    {
        var draft = new SubmissionDraft(Input(), ["user"]);

        draft.Questions.Should().BeEmpty();
        draft.Render([]).Should().Be(EcSubmission.ToJson(Input(), ["user"]));
    }

    [Fact]
    public void One_question_per_changed_feature()
    {
        new SubmissionDraft(Input(Session()), []).Questions.Should().Equal(Wmi2Feature.ChargeLimit, Wmi2Feature.CoolerBoost);
    }

    /// <summary>A block that will not be sent must not ask questions whose answers would go nowhere.</summary>
    [Theory]
    [InlineData("15M3EMS1.1090", "")]
    [InlineData("KUTAY-PC.109", "KUTAY-PC")]
    public void When_the_block_is_left_out_nothing_is_asked(string firmware, string privateWord)
    {
        var draft = new SubmissionDraft(Input(Session() with { Firmware = firmware }), privateWord.Length == 0 ? [] : [privateWord]);

        draft.Questions.Should().BeEmpty();
        JsonDocument.Parse(draft.Render([])).RootElement.TryGetProperty("experimental", out _).Should().BeFalse();
    }

    [Fact]
    public void Unanswered_questions_send_no_feedback()
    {
        Feedback(new SubmissionDraft(Input(Session()), []).Render([])).Should().BeEmpty();
    }

    [Fact]
    public void Answers_are_written_in_question_order_with_the_contract_words()
    {
        var draft = new SubmissionDraft(Input(Session()), []);

        var json = draft.Render([
            new Wmi2FeatureFeedback(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.NotWorked),
            new Wmi2FeatureFeedback(Wmi2Feature.ChargeLimit, Wmi2FeatureAnswer.Worked),
        ]);

        Feedback(json).Should().Equal(("charge_limit", "worked"), ("cooler_boost", "not_worked"));
    }

    [Fact]
    public void Not_tried_is_its_own_answer()
    {
        var json = new SubmissionDraft(Input(Session()), []).Render([new Wmi2FeatureFeedback(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.NotTried)]);

        Feedback(json).Should().Equal(("cooler_boost", "not_tried"));
    }

    [Fact]
    public void An_answer_for_a_feature_that_was_not_asked_about_is_left_out()
    {
        var json = new SubmissionDraft(Input(Session()), []).Render([new Wmi2FeatureFeedback(Wmi2Feature.FanMode, Wmi2FeatureAnswer.Worked)]);

        Feedback(json).Should().BeEmpty();
    }

    [Fact]
    public void Two_answers_for_one_feature_are_refused()
    {
        var draft = new SubmissionDraft(Input(Session()), []);

        var render = () => draft.Render([
            new Wmi2FeatureFeedback(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.Worked),
            new Wmi2FeatureFeedback(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.NotWorked),
        ]);

        render.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_same_answers_give_the_same_bytes()
    {
        var draft = new SubmissionDraft(Input(Session()), []);
        Wmi2FeatureFeedback[] answers = [new(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.Worked)];

        draft.Render(answers).Should().Be(draft.Render(answers));
    }

    [Fact]
    public void Answers_given_with_the_session_are_replaced_by_the_preview_answers()
    {
        var session = Session() with { Feedback = [new Wmi2FeatureFeedback(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.Worked)] };

        Feedback(new SubmissionDraft(Input(session), []).Render([])).Should().BeEmpty();
    }

    [Fact]
    public void Missing_input_or_answers_are_refused()
    {
        var noInput = () => new SubmissionDraft(null!, []);
        var noAnswers = () => new SubmissionDraft(Input(), []).Render(null!);

        noInput.Should().Throw<ArgumentNullException>();
        noAnswers.Should().Throw<ArgumentNullException>();
    }
}
