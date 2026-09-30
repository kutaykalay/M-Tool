using System.ComponentModel;
using System.Windows.Forms;
using MTool.App.Theme;
using MTool.App.ViewModels;
using MTool.Core.Device;

namespace MTool.App.Tray;

/// <summary>
/// The notification-area icon: tooltip with live temperatures, left click toggles the window, the
/// menu offers profiles, Cooler Boost, the window and exit. Also shows error balloons.
/// Created before the view model (which needs it as <see cref="INotifier"/>), then <see cref="Attach"/>ed.
/// </summary>
internal sealed class TrayIconHost : INotifier, IDisposable
{
    private const int BalloonMilliseconds = 10_000;
    private const int MaxBalloonText = 255;

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ThemeManager _theme;
    private readonly Action _toggleWindow;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private MainViewModel? _viewModel;

    public TrayIconHost(ThemeManager theme, Action toggleWindow, Action showWindow, Action exit)
    {
        (_theme, _toggleWindow, _showWindow, _exit) = (theme, toggleWindow, showWindow, exit);
        using var iconStream = typeof(TrayIconHost).Assembly.GetManifestResourceStream("MTool.m-tool.ico")
            ?? throw new InvalidOperationException("Gömülü tepsi ikonu bulunamadı.");
        _icon = new NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream, SystemInformation.SmallIconSize),
            Text = "M-Tool",
            ContextMenuStrip = _menu,
        };
        _menu.Renderer = TrayMenuRenderer.For(theme.IsDark);
        _menu.Opening += (_, e) => BuildMenu(e);
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _toggleWindow();
            }
        };
        _theme.Changed += OnThemeChanged;
    }

    public void Attach(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        _icon.Text = viewModel.TrayTooltip;
        _icon.Visible = true;
    }

    public void ShowError(string title, string message) =>
        _icon.ShowBalloonTip(BalloonMilliseconds, title, Truncate(message), ToolTipIcon.Error);

    public void Dispose()
    {
        _theme.Changed -= OnThemeChanged;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        // Without this the icon lingers in the tray until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TrayTooltip) && _viewModel is not null)
        {
            _icon.Text = _viewModel.TrayTooltip;
        }
    }

    private void OnThemeChanged() => _menu.Renderer = TrayMenuRenderer.For(_theme.IsDark);

    /// <summary>Rebuilt on every opening so checks and enabled states match the view model.</summary>
    private void BuildMenu(CancelEventArgs e)
    {
        foreach (var old in _menu.Items.Cast<ToolStripItem>().ToArray())
        {
            old.Dispose(); // Clear() alone keeps them alive
        }

        if (_viewModel?.Controls is not { } controls)
        {
            e.Cancel = true;
            return;
        }

        foreach (var name in controls.ProfileNames)
        {
            _menu.Items.Add(Item(name, name == controls.ActiveProfile, controls.CanWrite,
                () => Run(controls.SelectProfileCommand, name)));
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Cooler Boost", controls.CoolerBoostOn, controls.CanWrite,
            () => Run(controls.SetCoolerBoostCommand, !controls.CoolerBoostOn)));
        _menu.Items.Add(PerformanceMenu(controls));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Pencereyi aç", isChecked: false, enabled: true, _showWindow));
        _menu.Items.Add(Item("Çıkış", isChecked: false, enabled: true, _exit));
        e.Cancel = false;
    }

    private static ToolStripMenuItem PerformanceMenu(ControlsViewModel controls)
    {
        var menu = new ToolStripMenuItem($"Performans: {controls.PerformanceLabel}");
        foreach (var (mode, label) in new[] { (PerformanceMode.High, "Yüksek"), (PerformanceMode.Balanced, "Dengeli"), (PerformanceMode.Eco, "Pil") })
        {
            menu.DropDownItems.Add(Item(label, controls.ActivePerformance == mode, controls.CanWrite,
                () => Run(controls.SetPerformanceCommand, mode)));
        }

        return menu;
    }

    /// <summary>The menu was built when it opened; the state may have changed since (a write started, writes locked).</summary>
    private static void Run(System.Windows.Input.ICommand command, object parameter)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private static ToolStripMenuItem Item(string text, bool isChecked, bool enabled, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { Checked = isChecked, Enabled = enabled };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static string Truncate(string text) =>
        text.Length <= MaxBalloonText ? text : string.Concat(text.AsSpan(0, MaxBalloonText - 1), "…");
}
