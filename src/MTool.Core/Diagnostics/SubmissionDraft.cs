namespace MTool.Core.Diagnostics;

/// <summary>
/// What "Send EC data" previews before anything is sent: the collected parts, rendered again with the user's
/// answers each time one changes (plan asama15 15c-6). The preview shows a rendered text and sends exactly the
/// text it showed last, so the bytes sent are always bytes the user saw.
/// </summary>
public sealed class SubmissionDraft
{
    private readonly DeviceReportInput _input;
    private readonly IReadOnlyCollection<string> _privateWords;

    /// <param name="privateWords">The user and machine names, masked as in <see cref="EcSubmission.ToJson"/>.</param>
    public SubmissionDraft(DeviceReportInput input, IReadOnlyCollection<string> privateWords)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _privateWords = privateWords ?? throw new ArgumentNullException(nameof(privateWords));
        // A block that will be left out asks nothing: its answers would go nowhere.
        Questions = EcSubmission.IncludesExperimental(input.Experimental, privateWords) ? input.Experimental!.Questions : [];
    }

    /// <summary>One "worked / did not work / did not try" question per switch a write changed; empty when no block is sent.</summary>
    public IReadOnlyList<Wmi2Feature> Questions { get; }

    /// <summary>
    /// The payload with these answers in question order; an unanswered question sends nothing and an answer for a
    /// feature that was not asked about is left out.
    /// </summary>
    /// <exception cref="ArgumentException">Two answers for one feature.</exception>
    public string Render(IReadOnlyList<Wmi2FeatureFeedback> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.GroupBy(a => a.Feature).Any(g => g.Count() > 1))
        {
            throw new ArgumentException("One answer per feature.", nameof(answers));
        }

        if (_input.Experimental is not { } session)
        {
            return EcSubmission.ToJson(_input, _privateWords);
        }

        var feedback = Questions
            .Select(feature => answers.FirstOrDefault(a => a.Feature == feature))
            .OfType<Wmi2FeatureFeedback>()
            .ToList()
            .AsReadOnly();
        return EcSubmission.ToJson(_input with { Experimental = session with { Feedback = feedback } }, _privateWords);
    }
}
