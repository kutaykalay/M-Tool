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
