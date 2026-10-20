using System.Windows;
using MTool.App.Theme;
using MTool.App.ViewModels;

namespace MTool.App.Views;

/// <summary>
/// The settings window: General (the tray's switches and the language) and About. No logic here; the
/// device rows are loaded each time the window is shown (a failed write may have locked writing since).
/// </summary>
internal partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly ThemeManager _theme;

    public SettingsWindow(SettingsViewModel viewModel, ThemeManager theme)
    {
        (_viewModel, _theme) = (viewModel, theme);
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTheme();
        theme.Changed += ApplyTheme;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Brings the one window to the front, restoring it when minimised.</summary>
    public void ShowInFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        _theme.Changed -= ApplyTheme;
        base.OnClosed(e);
    }

    // LoadAsync never throws: a failure is logged and the rows stay as they were.
    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await _viewModel.About.LoadAsync();
        }
    }

    private void ApplyTheme() => TitleBar.ApplyTheme(this, _theme.IsDark);
}
