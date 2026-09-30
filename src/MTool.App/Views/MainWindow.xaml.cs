using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MTool.App.ViewModels;

namespace MTool.App.Views;

/// <summary>
/// Compact window next to the tray. Closing only hides it; <see cref="CloseForExit"/> really closes.
/// Tells the view model when it is shown or hidden so sensor polling speeds up or slows down.
/// </summary>
public partial class MainWindow : Window
{
    private const int ScreenMargin = 12;
    private const int DwmUseImmersiveDarkMode = 20;

    private readonly MainViewModel _viewModel;
    private bool _exiting;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        SizeChanged += (_, _) => MoveToCorner();
    }

    /// <summary>Shows and brings the window to the front, in the bottom-right corner of the work area.</summary>
    public void ShowNearTray()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        MoveToCorner();
        Activate();
    }

    public void ToggleFromTray()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            ShowNearTray();
        }
    }

    /// <summary>Safe to call more than once and after WPF closed the window during shutdown.</summary>
    public void CloseForExit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Close();
    }

    /// <summary>Matches the title bar to the app theme (Windows 11 honours this attribute).</summary>
    public void ApplyTitleBarTheme(bool dark)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await _viewModel.OnWindowShownAsync();
        }
        else
        {
            _viewModel.OnWindowHidden();
        }
    }

    private void MoveToCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Bottom - ActualHeight - ScreenMargin;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
