using MTool.Core.Diagnostics;
using MTool.App.ViewModels;

namespace MTool.Tests.Fakes;

/// <summary>Runs posted work at once, on the calling thread.</summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class FakeNotifier : INotifier
{
    public List<(string Title, string Message)> Errors { get; } = [];

    public List<(string Title, string Message)> Infos { get; } = [];

    /// <summary>The balloon throws, as a tray icon that is already gone would.</summary>
    public bool Fails { get; set; }

    public void ShowError(string title, string message) => Errors.Add((title, message));

    public void ShowInfo(string title, string message)
    {
        if (Fails)
        {
            throw new ObjectDisposedException("NotifyIcon");
        }

        Infos.Add((title, message));
    }
}

/// <summary>Keeps posted work until <see cref="RunAll"/>, like a UI thread that is busy elsewhere.</summary>
internal sealed class QueuedDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();

    public int Pending => _queue.Count;

    public void Post(Action action) => _queue.Enqueue(action);

    public void RunAll()
    {
        while (_queue.TryDequeue(out var action))
        {
            action();
        }
    }
}

/// <summary>Answers every question with <see cref="Answer"/> and records it.</summary>
internal sealed class FakeConfirm : IConfirm
{
    public bool Answer { get; set; } = true;

    /// <summary>Answers in order, one per question; <see cref="Answer"/> once they run out.</summary>
    public Queue<bool> Answers { get; } = new();

    public List<string> Questions { get; } = [];

    /// <summary>For each question, whether Enter would have answered yes.</summary>
    public List<bool> DefaultsToYes { get; } = [];

    /// <summary>Runs while the question is open, as a modal dialog lets other work run on the UI thread.</summary>
    public Action? WhileAsking { get; set; }

    public bool Ask(string question, bool defaultYes = true)
    {
        Questions.Add(question);
        WhileAsking?.Invoke();
        DefaultsToYes.Add(defaultYes);
        return Answers.TryDequeue(out var answer) ? answer : Answer;
    }
}

internal sealed class FakeSubmissionPreview : ISubmissionPreview
{
    public bool Answer { get; set; } = true;

    /// <summary>Every text rendered and shown, in order: first with no answers, then once per entry of <see cref="AnswerSteps"/>.</summary>
    public List<string> Shown { get; } = [];

    /// <summary>The questions of each draft shown.</summary>
    public List<IReadOnlyList<Wmi2Feature>> Asked { get; } = [];

    /// <summary>The answers after each click of the user; the preview renders again after each.</summary>
    public List<IReadOnlyList<Wmi2FeatureFeedback>> AnswerSteps { get; } = [];

    /// <summary>Runs while the preview is open, as the user would see the app.</summary>
    public Action? WhileOpen { get; set; }

    public string? Confirm(SubmissionDraft draft)
    {
        Asked.Add(draft.Questions);
        var text = draft.Render([]);
        Shown.Add(text);
        foreach (var answers in AnswerSteps)
        {
            text = draft.Render(answers);
            Shown.Add(text);
        }

        WhileOpen?.Invoke();
        return Answer ? text : null;
    }
}
