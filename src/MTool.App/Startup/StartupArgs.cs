namespace MTool.App.Startup;

internal enum StartupMode
{
    /// <summary>No arguments: the tray app with its window shown.</summary>
    Window,

    /// <summary><c>--tray</c> (sign-in task): the tray icon only; the window opens from the icon.</summary>
    TrayOnly,

    /// <summary>Any other arguments: command-line mode; unknown ones print the usage.</summary>
    CommandLine,
}

internal static class StartupArgs
{
    public const string Tray = "--tray";

    /// <summary>Case-sensitive, like every other option: <c>--TRAY</c> is a command-line typo, not the tray.</summary>
    public static StartupMode Parse(string[] args) => args switch
    {
        [] => StartupMode.Window,
        [Tray] => StartupMode.TrayOnly,
        _ => StartupMode.CommandLine,
    };
}
