using System.Windows;

namespace MTool.App.Views;

/// <summary>
/// A yes/no question in the app theme. Unlike a MessageBox it is a window the owner can close: closing it
/// counts as "no" (<see cref="Window.ShowDialog"/> returns false).
/// </summary>
internal partial class ConfirmDialog : Window
{
    public ConfirmDialog(string question, bool dark, bool defaultYes = true)
    {
        InitializeComponent();
        QuestionText.Text = question;
        YesButton.IsDefault = defaultYes;
        NoButton.IsDefault = !defaultYes;
        // Enter presses the button that has the focus, so the default answer must also hold the focus.
        Loaded += (_, _) => (defaultYes ? YesButton : NoButton).Focus();
        SourceInitialized += (_, _) => TitleBar.ApplyTheme(this, dark);
    }

    private void OnYesClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
