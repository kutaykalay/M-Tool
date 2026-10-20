namespace MTool.App.Views;

/// <summary>At most one settings window: made on first use, brought to the front after that.</summary>
internal sealed class SettingsWindowHost(Func<SettingsWindow> create)
{
    private SettingsWindow? _window;

    public void Show()
    {
        if (_window is null)
        {
            _window = create();
            _window.Closed += (_, _) => _window = null;
        }

        _window.ShowInFront();
    }

    public void CloseForExit() => _window?.Close();
}
