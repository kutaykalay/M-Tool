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

    public void ShowError(string title, string message) => Errors.Add((title, message));
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
