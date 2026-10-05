using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MTool.App.Theme;
using MTool.App.ViewModels;

namespace MTool.App.Views;

/// <summary>
/// The fan curve editor. No logic here: a typed number goes to <see cref="FanCurveEditorViewModel.MovePoint"/>,
/// which clamps it. Closing asks about a dirty draft, except on exit from the tray or when Windows ends the session.
/// </summary>
internal partial class FanCurveEditorWindow : Window
{
    private const string ThresholdBox = "up";

    private readonly FanCurveEditorViewModel _viewModel;
    private readonly ThemeManager _theme;
    private bool _exiting;
    private bool _closing;

    public FanCurveEditorWindow(FanCurveEditorViewModel viewModel, ThemeManager theme)
    {
        (_viewModel, _theme) = (viewModel, theme);
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTheme();
        theme.Changed += ApplyTheme;
        Application.Current.SessionEnding += OnSessionEnding;
    }

    /// <summary>The app is exiting: close without asking, the draft is lost.</summary>
    public void CloseForExit()
    {
        _exiting = true;
        if (!_closing) // the discard question may be open; answering it finishes the close
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        if (!_exiting)
        {
            // A pending value in the box being edited counts as part of the draft.
            CommitFocusedPoint();

            // _exiting again: the tray may have exited while the question was open.
            if (!_viewModel.ConfirmClose() && !_exiting)
            {
                e.Cancel = true;
                _closing = false;
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _theme.Changed -= ApplyTheme;
        Application.Current.SessionEnding -= OnSessionEnding;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e) => _exiting = true;

    private void ApplyTheme() => TitleBar.ApplyTheme(this, _theme.IsDark);

    private void OnCpuClick(object sender, RoutedEventArgs e) => _viewModel.SelectedFan = CurveFan.Cpu;

    private void OnGpuClick(object sender, RoutedEventArgs e) => _viewModel.SelectedFan = CurveFan.Gpu;

    /// <summary>
    /// A click on a button that is still disabled (Kaydet before the draft is dirty) takes no focus, so
    /// the box would never lose it: the value is taken first, which enables the button for this click.
    /// </summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target && !IsInsideTextBox(target))
        {
            CommitFocusedPoint();
        }
    }

    private void OnPointLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Tab to the next box: after a change the rows are rebuilt, so the focus is given back to it.
        (int, object?)? next = e.NewFocus is TextBox { DataContext: PointViewModel nextPoint } nextBox
            ? (nextPoint.Index, nextBox.Tag)
            : null;
        Commit((TextBox)sender, next);
    }

    private void OnPointKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: PointViewModel point } box)
        {
            Commit(box, (point.Index, box.Tag));
            e.Handled = true;
        }
    }

    private void CommitFocusedPoint()
    {
        if (Keyboard.FocusedElement is TextBox { DataContext: PointViewModel } box)
        {
            Commit(box, refocus: null);
        }
    }

    private void Commit(TextBox box, (int Index, object? Tag)? refocus)
    {
        if (box.IsReadOnly || box.DataContext is not PointViewModel point)
        {
            return;
        }

        var isThreshold = Equals(box.Tag, ThresholdBox);
        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            || value == (isThreshold ? point.UpC : point.SpeedPercent))
        {
            // Not a number, or the same number written differently ("050"): show the point again.
            // An unchanged value does not rebuild the rows, so the focus moves on as usual.
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            return;
        }

        _viewModel.MovePoint(
            _viewModel.SelectedFan,
            point.Index,
            isThreshold ? value : point.UpC,
            isThreshold ? point.SpeedPercent : value);
        if (refocus is { } target)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusPointBox(target.Index, target.Tag));
        }
    }

    private void FocusPointBox(int index, object? tag)
    {
        if (PointRows.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject row
            && FindTextBox(row, tag) is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private static bool IsInsideTextBox(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private static TextBox? FindTextBox(DependencyObject node, object? tag)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if ((child is TextBox box && Equals(box.Tag, tag) ? box : FindTextBox(child, tag)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
