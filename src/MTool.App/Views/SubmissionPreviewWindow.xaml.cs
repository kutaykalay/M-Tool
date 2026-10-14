using System.Windows;

namespace MTool.App.Views;

/// <summary>
/// The full text "Send EC data" would send, read-only, with "Don't send" as the default (Enter and Esc).
/// Closing the window, or the owner closing it, counts as "Don't send" (<see cref="Window.ShowDialog"/> returns false).
/// </summary>
internal partial class SubmissionPreviewWindow : Window
{
    /// <param name="owner">The visible window that asked; null when M-Tool is only in the tray.</param>
    public SubmissionPreviewWindow(string text, bool dark, Window? owner)
    {
        InitializeComponent();
        PayloadText.Text = text;
        Owner = owner;
        // Opened from the tray it has no owner: it must not come up behind other windows while the menu waits.
        Topmost = owner is null;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        // Enter presses the button that has the focus, so the default answer holds it, not the text.
        Loaded += (_, _) =>
        {
            Activate();
            DontSendButton.Focus();
        };
        SourceInitialized += (_, _) => TitleBar.ApplyTheme(this, dark);
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
