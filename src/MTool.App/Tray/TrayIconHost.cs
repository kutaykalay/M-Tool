using System.ComponentModel;
using System.Windows.Forms;
using MTool.App.Theme;
using MTool.App.ViewModels;
using static MTool.App.Tray.TrayDeviceItems;

namespace MTool.App.Tray;

/// <summary>
/// The notification-area icon: tooltip with live temperatures, left click toggles the window, the
/// menu offers profiles, Cooler Boost, performance, AC/battery switching, the window and exit. Also shows error balloons.
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
    private SignInStartViewModel? _signInStart;

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

    public void Attach(MainViewModel viewModel, SignInStartViewModel signInStart)
    {
        _viewModel = viewModel;
        _signInStart = signInStart;
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

        _menu.Items.AddRange([.. TrayDeviceItems.For(controls)]);

        // Only changes the settings file, so it stays enabled while writing is locked.
        var powerSwitch = _viewModel.PowerSwitch;
        _menu.Items.Add(Item("Prizde ve pilde ayrı ayar", powerSwitch.IsOn, powerSwitch.ToggleCommand.CanExecute(null),
            () => Run(powerSwitch.ToggleCommand, null)));
        _menu.Items.Add(new ToolStripSeparator());
        if (_signInStart is { } signInStart)
        {
            _menu.Items.Add(Item("Oturum açılışında başlat", signInStart.IsEnabled, signInStart.ToggleCommand.CanExecute(null),
                () => Run(signInStart.ToggleCommand, null)));
        }

        _menu.Items.Add(Item("Pencereyi aç", isChecked: false, enabled: true, _showWindow));
        _menu.Items.Add(Item("Çıkış", isChecked: false, enabled: true, _exit));
        e.Cancel = false;
    }

    private static string Truncate(string text) =>
        text.Length <= MaxBalloonText ? text : string.Concat(text.AsSpan(0, MaxBalloonText - 1), "…");
}
