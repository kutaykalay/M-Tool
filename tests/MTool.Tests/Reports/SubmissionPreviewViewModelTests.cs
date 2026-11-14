using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Reports;

/// <summary>
/// The "Send EC data" preview (plan asama15 15c-6): one "worked / did not work / did not try" question per switch a
/// write changed, the text rendered again on every answer, and the text shown last is the text sent.
/// </summary>
public sealed class SubmissionPreviewViewModelTests
{
    private static DeviceReportInput Input(ExperimentalSession? experimental) => new(
        "0.18.0-beta.1",
        new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero),
        Section.Ok(new SystemInfo("Micro-Star International Co., Ltd.", "Katana GF66 11UE", null, null, null, null, null)),
        WmiProbeOutcome.Answered(WmiInterface.Wmi2),
        Section.Ok<IReadOnlyList<WmiClassInfo>>([]),
        MsiInfo.Unknown,
        Section.Ok(0),
        Section.Failed<EcReadout>("not read"),
        Experimental: experimental);

    private static SubmissionDraft Draft(params Wmi2WriteRecord[] writes) => new(
        Input(writes.Length == 0 ? null : new ExperimentalSession("15M3EMS1.109", new Wmi2Status(0x02, 0xC1, 0x0D, 0xE4), true, writes, [])), []);

    private static SubmissionDraft TwoSwitches() => Draft(
        new Wmi2WriteRecord(Wmi2Feature.CoolerBoost, 0x02, 0x82, Wmi2ReadBack.Ok),
        new Wmi2WriteRecord(Wmi2Feature.ChargeLimit, 0xE4, 0xD0, Wmi2ReadBack.Ok));

    [Fact]
    public void Without_experimental_writes_there_are_no_questions_and_the_text_is_the_payload()
    {
        var draft = Draft();

        var preview = new SubmissionPreviewViewModel(draft);

        preview.Questions.Should().BeEmpty();
        preview.HasQuestions.Should().BeFalse();
        preview.Text.Should().Be(draft.Render([]));
    }

    [Fact]
    public void One_question_per_changed_switch_named_as_in_the_WMI2_window_and_none_answered()
    {
        var preview = new SubmissionPreviewViewModel(TwoSwitches());

        preview.HasQuestions.Should().BeTrue();
        preview.Questions.Select(q => q.Feature).Should().Equal(Wmi2Feature.CoolerBoost, Wmi2Feature.ChargeLimit);
        preview.Questions.Select(q => q.Label).Should().Equal(Strings.Wmi2_CoolerBoost, Strings.Wmi2_ChargeLimit);
        preview.Questions.Should().OnlyContain(q => q.Answer == null);
    }

    [Fact]
    public void Each_answer_renders_the_text_again_with_it()
    {
        var draft = TwoSwitches();
        var preview = new SubmissionPreviewViewModel(draft);
        var changes = new List<string?>();
        preview.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        preview.Questions[1].ChooseCommand.Execute(Wmi2FeatureAnswer.NotTried);
        preview.Questions[0].ChooseCommand.Execute(Wmi2FeatureAnswer.Worked);

        preview.Questions[0].Answer.Should().Be(Wmi2FeatureAnswer.Worked);
        preview.Text.Should().Be(draft.Render([
            new Wmi2FeatureFeedback(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.Worked),
            new Wmi2FeatureFeedback(Wmi2Feature.ChargeLimit, Wmi2FeatureAnswer.NotTried),
        ]));
        preview.Text.Should().Contain("\"user\": \"worked\"");
        changes.Should().Contain(nameof(SubmissionPreviewViewModel.Text));
    }

    [Fact]
    public void Changing_an_answer_replaces_it()
    {
        var draft = TwoSwitches();
        var preview = new SubmissionPreviewViewModel(draft);

        preview.Questions[0].ChooseCommand.Execute(Wmi2FeatureAnswer.Worked);
        preview.Questions[0].ChooseCommand.Execute(Wmi2FeatureAnswer.NotWorked);

        preview.Text.Should().Be(draft.Render([new Wmi2FeatureFeedback(Wmi2Feature.CoolerBoost, Wmi2FeatureAnswer.NotWorked)]));
    }

    [Fact]
    public void The_three_answers_have_their_own_words()
    {
        new[] { Strings.Submit_FeedbackWorked, Strings.Submit_FeedbackNotWorked, Strings.Submit_FeedbackNotTried, Strings.Submit_FeedbackIntro }
            .Should().OnlyHaveUniqueItems().And.NotContain(s => string.IsNullOrWhiteSpace(s));
    }
}
