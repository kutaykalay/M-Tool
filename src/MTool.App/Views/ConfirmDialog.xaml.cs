using System.Windows;

namespace MTool.App.Views;

/// <summary>
/// A yes/no question in the app theme. Unlike a MessageBox it is a window the owner can close: closing it
/// counts as "no" (<see cref="Window.ShowDialog"/> returns false).
/// </summary>
internal partial class ConfirmDialog : Window
{
    public ConfirmDialog(string question, bool dark)
    {
        InitializeComponent();
        QuestionText.Text = question;
        SourceInitialized += (_, _) => TitleBar.ApplyTheme(this, dark);
    }

    private void OnYesClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
