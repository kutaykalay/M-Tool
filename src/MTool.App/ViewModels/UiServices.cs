namespace MTool.App.ViewModels;

/// <summary>Moves work to the UI thread (sensor events arrive on thread-pool threads).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>A yes/no question to the user (a dialog in the app).</summary>
public interface IConfirm
{
    /// <param name="defaultYes">Whether Enter answers yes. Pass false for a question where yes is the risky answer.</param>
    bool Ask(string question, bool defaultYes = true);
}

/// <summary>Shows the full text "Send EC data" would send; nothing is sent unless the user chooses to.</summary>
public interface ISubmissionPreview
{
    /// <returns>True only when the user chose Send; the default button is Don't send.</returns>
    bool Confirm(string text);
}

/// <summary>Tray balloon for problems the user must see even with the window closed.</summary>
public interface INotifier
{
    void ShowError(string title, string message);

    /// <summary>A balloon with the information icon, for good news the user should see even with the window closed.</summary>
    void ShowInfo(string title, string message);
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
