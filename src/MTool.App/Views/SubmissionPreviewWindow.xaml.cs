using System.Windows;

namespace MTool.App.Views;

/// <summary>
/// The full text "Send EC data" would send, read-only, with "Don't send" as the default (Enter and Esc).
/// Closing the window, or the owner closing it, counts as "Don't send" (<see cref="Window.ShowDialog"/> returns false).
/// </summary>
internal partial class SubmissionPreviewWindow : Window
{
    public SubmissionPreviewWindow(string text, bool dark)
    {
        InitializeComponent();
        PayloadText.Text = text;
        // Enter presses the button that has the focus, so the default answer holds it, not the text.
        Loaded += (_, _) => DontSendButton.Focus();
        SourceInitialized += (_, _) => TitleBar.ApplyTheme(this, dark);
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
