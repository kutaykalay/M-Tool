using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Resources;
using MTool.Core.Diagnostics;

namespace MTool.App.ViewModels;

/// <summary>
/// The "Send EC data" preview (plan asama15 15c-6). After an experimental WMI2 write it asks, once per switch a write
/// changed, whether the switch worked; nothing is chosen until the user chooses, and an unanswered question sends
/// nothing. Every answer renders <see cref="Text"/> again, and the window sends exactly <see cref="Text"/>, so the
/// bytes sent are the bytes shown.
/// </summary>
internal sealed partial class SubmissionPreviewViewModel : ObservableObject
{
    private readonly SubmissionDraft _draft;

    public SubmissionPreviewViewModel(SubmissionDraft draft)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Questions = draft.Questions.Select(f => new SubmissionQuestion(f, LabelOf(f), Render)).ToList().AsReadOnly();
        Text = draft.Render([]);
    }

    public IReadOnlyList<SubmissionQuestion> Questions { get; }

    public bool HasQuestions => Questions.Count > 0;

    /// <summary>The text shown, and the text sent on Send.</summary>
    [ObservableProperty]
    public partial string Text { get; private set; }

    private void Render() =>
        Text = _draft.Render(Questions
            .Where(q => q.Answer is not null)
            .Select(q => new Wmi2FeatureFeedback(q.Feature, q.Answer!.Value))
            .ToList());

    /// <summary>The names the WMI2 window uses for its switches.</summary>
    private static string LabelOf(Wmi2Feature feature) => feature switch
    {
        Wmi2Feature.CoolerBoost => Strings.Wmi2_CoolerBoost,
        Wmi2Feature.PerformanceMode => Strings.Wmi2_PerformanceMode,
        Wmi2Feature.ChargeLimit => Strings.Wmi2_ChargeLimit,
        _ => Strings.Wmi2_FanMode,
    };
}

/// <summary>One switch's question in the preview.</summary>
internal sealed partial class SubmissionQuestion(Wmi2Feature feature, string label, Action answered) : ObservableObject
{
    public Wmi2Feature Feature { get; } = feature;

    public string Label { get; } = label;

    /// <summary>Null until the user chooses.</summary>
    [ObservableProperty]
    public partial Wmi2FeatureAnswer? Answer { get; private set; }

    [RelayCommand]
    private void Choose(Wmi2FeatureAnswer answer)
    {
        Answer = answer;
        answered();
    }
}
