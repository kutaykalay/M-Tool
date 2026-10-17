using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MTool.App.ViewModels;

namespace MTool.App.Views;

/// <summary>
/// Shown instead of M-Tool on a laptop it cannot control yet (no WMI1): why, and the report actions, so
/// the owner can still send what M-Tool needs to learn the model. It never opens the port or the gateway;
/// a report reads the EC through WMI only, as on the command line. On a WMI2 laptop it also has the
/// experimental live values (read only, after the user says yes). Closing it ends M-Tool.
/// </summary>
internal partial class ReportOnlyWindow : Window
{
    private const int ScreenMargin = 12;

    public ReportOnlyWindow(string problem, ReportViewModel report, StatusViewModel status, Wmi2LiveViewModel? live, bool dark)
    {
        InitializeComponent();
        ProblemText.Text = problem;
        DataContext = new Context(report, status, live);
        SourceInitialized += (_, _) =>
        {
            TitleBar.ApplyTheme(this, dark);
            MaxHeight = ScreenFit.MaxHeight(ScreenFit.InUnits(WorkAreaPixels(), VisualTreeHelper.GetDpi(this)), ScreenMargin);
        };
        SizeChanged += (_, _) => KeepInside(); // a message band or the live values make it taller
    }

    /// <summary>What the bindings read: the report commands, the message band and, on a WMI2 laptop, the live values.</summary>
    internal sealed record Context(ReportViewModel Report, StatusViewModel Status, Wmi2LiveViewModel? Live);

    /// <summary>After a display scale change the window may not fit any more: centre it again once laid out.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            Place(ScreenFit.Centered(WorkAreaPixels(), VisualTreeHelper.GetDpi(this), new Size(ActualWidth, ActualHeight), ScreenMargin)));
    }

    private void KeepInside()
    {
        if (IsLoaded)
        {
            Place(ScreenFit.KeepInside(WorkAreaPixels(), VisualTreeHelper.GetDpi(this), new Rect(Left, Top, ActualWidth, ActualHeight), ScreenMargin));
        }
    }

    private void Place(WindowPlace place)
    {
        MaxHeight = place.MaxHeight;
        Left = place.Left;
        Top = place.Top;
    }

    // The live work area in pixels of the screen the window is on: SystemParameters.WorkArea keeps the scale the app started with.
    private Rect WorkAreaPixels()
    {
        var pixels = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        return new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height);
    }
}
