using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MTool.App.ViewModels;

namespace MTool.App.Views;

/// <summary>
/// Compact window next to the tray. Closing only hides it; <see cref="CloseForExit"/> really closes.
/// Tells the view model when it is shown or hidden so sensor polling speeds up or slows down.
/// </summary>
internal partial class MainWindow : Window
{
    private const int ScreenMargin = 12;

    private readonly MainViewModel _viewModel;
    private readonly Func<FanCurveEditorWindow> _createEditor;
    private FanCurveEditorWindow? _editor;
    private bool _exiting;

    public MainWindow(MainViewModel viewModel, Func<FanCurveEditorWindow> createEditor)
    {
        (_viewModel, _createEditor) = (viewModel, createEditor);
        DataContext = viewModel;
        InitializeComponent();
        HookBacklightBar();
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
        _editor?.CloseForExit();
        Close();
    }

    /// <summary>One editor at a time: a second click brings the open one to the front.</summary>
    private void OnEditCurvesClick(object sender, RoutedEventArgs e)
    {
        if (_editor is null)
        {
            _editor = _createEditor();
            _editor.Closed += (_, _) => _editor = null;
        }

        if (_editor.WindowState == WindowState.Minimized)
        {
            _editor.WindowState = WindowState.Normal;
        }

        _editor.Show();
        _editor.Activate();
    }

    /// <summary>
    /// A click or drag on the keyboard backlight bar writes once, on release. handledEventsToo: with
    /// IsMoveToPointEnabled the Slider handles a track click itself, so a plain handler would miss the
    /// press. The lost capture covers a release outside the window; a second end is ignored.
    /// </summary>
    private void HookBacklightBar()
    {
        BacklightBar.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, _) => _viewModel.Controls.BeginKeyboardBacklightDrag()), handledEventsToo: true);
        BacklightBar.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler((_, _) => _viewModel.Controls.EndKeyboardBacklightDrag()), handledEventsToo: true);
        BacklightBar.AddHandler(LostMouseCaptureEvent,
            new MouseEventHandler((_, _) => _viewModel.Controls.EndKeyboardBacklightDrag()), handledEventsToo: true);
    }

    public void ApplyTitleBarTheme(bool dark) => TitleBar.ApplyTheme(this, dark);

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

    /// <summary>After a display scale change the old corner may be off screen: place it again once laid out.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, MoveToCorner);
    }

    // The live work area in pixels: SystemParameters.WorkArea keeps the scale the app started with.
    private void MoveToCorner()
    {
        if (System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea is not { } pixels)
        {
            return;
        }

        var corner = TrayPlacement.Corner(
            new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height),
            VisualTreeHelper.GetDpi(this),
            new Size(ActualWidth, ActualHeight),
            ScreenMargin);
        MaxHeight = corner.MaxHeight;
        Left = corner.Left;
        Top = corner.Top;
    }
}
