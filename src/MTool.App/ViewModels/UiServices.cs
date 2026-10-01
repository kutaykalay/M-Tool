namespace MTool.App.ViewModels;

/// <summary>Moves work to the UI thread (sensor events arrive on thread-pool threads).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Tray balloon for problems the user must see even with the window closed.</summary>
public interface INotifier
{
    void ShowError(string title, string message);
}

public enum MessageKind
{
    None,
    Info,
    Warning,
    Error,
}

/// <summary>The sign-in task Windows keeps for M-Tool; the task itself is the source of truth.</summary>
public interface IStartupTask
{
    /// <summary>The exe the task starts; "" if it starts none; null when there is no task.</summary>
    string? QueryRegisteredExe();

    void Enable(string exePath);

    void Disable();
}
