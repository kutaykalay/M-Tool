using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.App.Views.Chart;
using MTool.Core.Profiles;

namespace MTool.App.Views;

/// <summary>Where the user asked a chart point to go; the editor clamps it.</summary>
internal sealed record PointMove(int Index, int UpC, int SpeedPercent);

/// <summary>
/// The fan curve as a chart with seven draggable points. No curve rules here: a drag or an arrow key
/// raises <see cref="MoveRequested"/>, the window passes it to the editor, and each point is drawn
/// where <see cref="Points"/> says, so a clamped or ignored move puts it back by itself.
/// </summary>
internal partial class FanCurveCanvas : UserControl
{
    public static readonly DependencyProperty PointsProperty = Register(nameof(Points), typeof(IReadOnlyList<PointViewModel>), null);
    public static readonly DependencyProperty OtherCurveProperty = Register(nameof(OtherCurve), typeof(FanCurve), null);
    public static readonly DependencyProperty SelectedFanProperty = Register(nameof(SelectedFan), typeof(CurveFan), CurveFan.Cpu);
    public static readonly DependencyProperty IsReadOnlyProperty = Register(nameof(IsReadOnly), typeof(bool), false);

    private const double LeftMargin = 36;
    private const double RightMargin = 10;
    private const double TopMargin = ThumbRadius;
    private const double BottomMargin = 20;
    private const double ThumbRadius = 10;
    private const double MinLimitsWidth = 8;
    private const string SecondaryBrush = "Brush.ChartSecondary";
    private const string PrimaryBrush = "Brush.Accent";

    private readonly Polyline _other = new() { StrokeThickness = 1.5, Opacity = 0.35, IsHitTestVisible = false };
    private readonly Polyline _curve = new() { StrokeThickness = 2.5, IsHitTestVisible = false };
    private readonly Rectangle _limits = new() { Opacity = 0.12, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly List<Line> _markers = [];
    private readonly List<Thumb> _thumbs = [];
    private (int UpC, int Pct) _dragStart;
    private (int UpC, int Pct) _lastSent;
    private Vector _grabOffset;

    public FanCurveCanvas()
    {
        InitializeComponent();
        Surface.Children.Add(_limits);
        Surface.Children.Add(_other);
        for (var i = 1; i < CurveValidator.PointCount; i++)
        {
            var marker = new Line { StrokeThickness = 1.2, StrokeDashArray = [3, 3], IsHitTestVisible = false };
            _markers.Add(marker);
            Surface.Children.Add(marker);
        }

        Surface.Children.Add(_curve);
        for (var i = 0; i < CurveValidator.PointCount; i++)
        {
            var thumb = new Thumb { Style = (Style)FindResource("PointThumb"), Tag = i };
            thumb.DragStarted += OnDragStarted;
            thumb.DragDelta += OnDragDelta;
            thumb.KeyDown += OnThumbKeyDown;
            thumb.GotKeyboardFocus += (_, _) => Redraw();
            thumb.LostKeyboardFocus += (_, _) => Redraw();
            _thumbs.Add(thumb);
            Surface.Children.Add(thumb);
        }
    }

    public event EventHandler<PointMove>? MoveRequested;

    public IReadOnlyList<PointViewModel>? Points
    {
        get => (IReadOnlyList<PointViewModel>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public FanCurve? OtherCurve
    {
        get => (FanCurve?)GetValue(OtherCurveProperty);
        set => SetValue(OtherCurveProperty, value);
    }

    public CurveFan SelectedFan
    {
        get => (CurveFan)GetValue(SelectedFanProperty);
        set => SetValue(SelectedFanProperty, value);
    }

    /// <summary>A built-in profile: the points can be focused and read, not moved.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private Rect Plot => new(
        LeftMargin,
        TopMargin,
        Math.Max(0, Surface.ActualWidth - LeftMargin - RightMargin),
        Math.Max(0, Surface.ActualHeight - TopMargin - BottomMargin));

    private static DependencyProperty Register(string name, Type type, object? defaultValue) => DependencyProperty.Register(
        name, type, typeof(FanCurveCanvas), new PropertyMetadata(defaultValue, (d, _) => ((FanCurveCanvas)d).Redraw()));

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawGrid();
        Redraw();
    }

    private void DrawGrid()
    {
        GridLayer.Children.Clear();
        var plot = Plot;
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        foreach (var c in CurveChartScale.XTicks)
        {
            var x = CurveChartScale.ToPixel(c, 0, plot).X;
            AddGridLine(x, plot.Top, x, plot.Bottom);
            AddLabel(c.ToString(CultureInfo.CurrentCulture), x, plot.Bottom + 2, centerX: true);
        }

        foreach (var pct in CurveChartScale.YTicks)
        {
            var y = CurveChartScale.ToPixel(CurveChartScale.MinC, pct, plot).Y;
            AddGridLine(plot.Left, y, plot.Right, y);
            AddLabel(string.Format(Strings.Chart_PercentTick, pct), plot.Left - 4, y, centerX: false);
        }
    }

    private void AddGridLine(double x1, double y1, double x2, double y2)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1, SnapsToDevicePixels = true };
        line.SetResourceReference(Shape.StrokeProperty, "Brush.Border");
        GridLayer.Children.Add(line);
    }

    // centerX: below a vertical grid line; otherwise right-aligned to x and centred on y.
    private void AddLabel(string text, double x, double y, bool centerX)
    {
        var label = new TextBlock { Text = text, Style = (Style)FindResource("Muted"), FontSize = 11 };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = label.DesiredSize;
        Canvas.SetLeft(label, centerX ? x - (size.Width / 2) : x - size.Width);
        Canvas.SetTop(label, centerX ? y : y - (size.Height / 2));
        GridLayer.Children.Add(label);
    }

    private void Redraw()
    {
        AutomationProperties.SetName(this, string.Format(Strings.Chart_Name, SelectedFan == CurveFan.Cpu ? "CPU" : "GPU"));
        var plot = Plot;
        if (Points is not { Count: CurveValidator.PointCount } points || plot.Width <= 0 || plot.Height <= 0)
        {
            Surface.Visibility = Visibility.Hidden;
            return;
        }

        Surface.Visibility = Visibility.Visible;
        var (main, other) = SelectedFan == CurveFan.Cpu ? (PrimaryBrush, SecondaryBrush) : (SecondaryBrush, PrimaryBrush);

        var curve = FanCurve.Of([.. points.Select(p => (p.UpC, p.SpeedPercent))]);
        SetLine(_curve, CurveChartScale.StepVertices(curve), main, plot);
        SetLine(_other, OtherCurve is { } otherCurve ? CurveChartScale.StepVertices(otherCurve) : [], other, plot);

        var markers = CurveChartScale.DownMarkers(curve, [.. points.Skip(1).Select(p => p.DownC ?? p.UpC)]);
        for (var i = 0; i < _markers.Count; i++)
        {
            var marker = markers[i];
            var from = CurveChartScale.ToPixel(marker.C, marker.FromPct, plot);
            var to = CurveChartScale.ToPixel(marker.C, marker.ToPct, plot);
            (_markers[i].X1, _markers[i].Y1, _markers[i].X2, _markers[i].Y2) = (from.X, from.Y, to.X, to.Y);
            _markers[i].SetResourceReference(Shape.StrokeProperty, main);
        }

        for (var i = 0; i < _thumbs.Count; i++)
        {
            PlaceThumb(_thumbs[i], points[i], main, plot);
        }

        DrawLimits(points, main, plot);
    }

    private static void SetLine(Polyline line, IReadOnlyList<(double C, double Pct)> vertices, string brush, Rect plot)
    {
        line.Points = [.. vertices.Select(v => CurveChartScale.ToPixel(v.C, v.Pct, plot))];
        line.SetResourceReference(Shape.StrokeProperty, brush);
    }

    private void PlaceThumb(Thumb thumb, PointViewModel point, string brush, Rect plot)
    {
        var (c, pct) = CurveChartScale.PointPosition(point);
        var pixel = CurveChartScale.ToPixel(c, pct, plot);
        Canvas.SetLeft(thumb, pixel.X - ThumbRadius);
        Canvas.SetTop(thumb, pixel.Y - ThumbRadius);
        Panel.SetZIndex(thumb, thumb.IsKeyboardFocused ? 1 : 0);
        thumb.SetResourceReference(ForegroundProperty, brush);
        thumb.Cursor = IsReadOnly ? Cursors.Arrow : Cursors.SizeAll;
        AutomationProperties.SetName(thumb, point.AccessibleName);
        AutomationProperties.SetHelpText(thumb, IsReadOnly ? Strings.Editor_BuiltInReadOnly : point.HelpText);
    }

    // The box the focused point may move in, so a point that stops shows why.
    private void DrawLimits(IReadOnlyList<PointViewModel> points, string brush, Rect plot)
    {
        var focused = _thumbs.FindIndex(t => t.IsKeyboardFocused);
        if (IsReadOnly || focused < 0 || CurveChartScale.LimitsArea(points[focused]) is not { } area)
        {
            _limits.Visibility = Visibility.Collapsed;
            return;
        }

        // The idle point's area is a line on the left edge: drawn as a narrow strip around it.
        var topLeft = CurveChartScale.ToPixel(area.MinC, area.MaxPct, plot);
        var bottomRight = CurveChartScale.ToPixel(area.MaxC, area.MinPct, plot);
        var width = bottomRight.X - topLeft.X;
        _limits.Width = Math.Max(MinLimitsWidth, width);
        _limits.Height = Math.Max(1, bottomRight.Y - topLeft.Y);
        Canvas.SetLeft(_limits, width < MinLimitsWidth ? topLeft.X + (width / 2) - (MinLimitsWidth / 2) : topLeft.X);
        Canvas.SetTop(_limits, topLeft.Y);
        _limits.SetResourceReference(Shape.FillProperty, brush);
        _limits.Visibility = Visibility.Visible;
    }

    // A click focuses the point even on a built-in profile, so it can be read; only the drag is refused.
    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        var thumb = (Thumb)sender;
        thumb.Focus();
        if (IsReadOnly || Points is not { Count: CurveValidator.PointCount } points)
        {
            thumb.CancelDrag();
            return;
        }

        var point = points[(int)thumb.Tag];
        _dragStart = _lastSent = (point.UpC, point.SpeedPercent);
        var centre = new Point(Canvas.GetLeft(thumb) + ThumbRadius, Canvas.GetTop(thumb) + ThumbRadius);
        _grabOffset = Mouse.GetPosition(Surface) - centre;
    }

    // The absolute mouse position (less where the point was grabbed), not the drag delta: a clamped
    // point would drift away from the mouse.
    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var thumb = (Thumb)sender;
        if (CurveChartScale.ToValue(Mouse.GetPosition(Surface) - _grabOffset, Plot) is not { } value
            || Points is not { Count: CurveValidator.PointCount } points)
        {
            return;
        }

        var point = points[(int)thumb.Tag];
        Request(point.Index, point.IsIdle ? point.UpC : value.C, value.Pct);
    }

    private void OnThumbKeyDown(object sender, KeyEventArgs e)
    {
        var thumb = (Thumb)sender;
        if (e.Key == Key.Escape && thumb.IsDragging)
        {
            thumb.CancelDrag();
            _lastSent = _dragStart;
            MoveRequested?.Invoke(this, new PointMove((int)thumb.Tag, _dragStart.UpC, _dragStart.Pct));
            e.Handled = true;
            return;
        }

        if (Points is not { Count: CurveValidator.PointCount } points
            || CurveChartScale.KeyStep(points[(int)thumb.Tag], e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) is not { } target)
        {
            return;
        }

        // An arrow is always handled, or WPF would move the focus to a neighbour.
        e.Handled = true;
        if (!IsReadOnly)
        {
            MoveRequested?.Invoke(this, new PointMove((int)thumb.Tag, target.UpC, target.Pct));
        }
    }

    // One request per new value, not per mouse pixel.
    private void Request(int index, int upC, int pct)
    {
        if ((upC, pct) == _lastSent)
        {
            return;
        }

        _lastSent = (upC, pct);
        MoveRequested?.Invoke(this, new PointMove(index, upC, pct));
    }
}
