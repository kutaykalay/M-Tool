namespace MTool.App.ViewModels;

/// <summary>
/// Where there is no tray icon (the reports-only window), what would be a balloon goes to the window's
/// message band: good news as a notice, a problem as a warning. The title is dropped; the window has one.
/// </summary>
internal sealed class BandNotifier(StatusViewModel status) : INotifier
{
    /// <summary>Shows nothing: for the band's own <see cref="StatusViewModel"/>, which must not notify back into itself.</summary>
    public static INotifier Silent { get; } = new SilentNotifier();

    public void ShowError(string title, string message) => status.ShowWarning(message);

    public void ShowInfo(string title, string message) => status.ShowInfo(message);

    private sealed class SilentNotifier : INotifier
    {
        public void ShowError(string title, string message)
        {
        }

        public void ShowInfo(string title, string message)
        {
        }
    }
}
