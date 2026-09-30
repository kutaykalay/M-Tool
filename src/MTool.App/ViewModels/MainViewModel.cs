using CommunityToolkit.Mvvm.ComponentModel;
using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Core.Sensors;
using TooltipText = MTool.App.Tray.TrayTooltip;

namespace MTool.App.ViewModels;

/// <summary>Ties the window, the tray tooltip and the sensor poller together.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>plan.md §3: 1 s with the window open, 5 s for the tray tooltip.</summary>
    public static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(5);

    private readonly SensorPoller _poller;

    public MainViewModel(
        SensorPoller poller, ProfileService service, IP65Control control, ProfileCatalog catalog, INotifier notifier, IUiDispatcher ui)
    {
        _poller = poller;
        Status = new StatusViewModel(notifier);
        Controls = new ControlsViewModel(service, control, catalog, Status);
        TrayTooltip = TooltipText.Format(poller.Latest, Controls.ActiveProfileLabel);

        poller.ReadingChanged += reading => ui.Post(() =>
        {
            Sensors.Apply(reading);
            UpdateTooltip();
        });

        // The tooltip names the profile the EC holds, not the one asked for (they differ in dry-run
        // and after a reboot).
        Controls.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ControlsViewModel.ActiveProfileLabel))
            {
                UpdateTooltip();
            }
        };
    }

    public SensorsViewModel Sensors { get; } = new();

    public ControlsViewModel Controls { get; }

    public StatusViewModel Status { get; }

    [ObservableProperty]
    public partial string TrayTooltip { get; private set; }

    /// <param name="startupWarnings">Settings problems found while loading (shown once).</param>
    public async Task InitializeAsync(IReadOnlyList<string> startupWarnings)
    {
        if (startupWarnings.Count > 0)
        {
            Status.ShowWarning(string.Join(" ", startupWarnings));
        }

        await Controls.RefreshAsync();
        UpdateTooltip();
    }

    public async Task OnWindowShownAsync()
    {
        _poller.SetInterval(VisibleInterval);
        _ = _poller.PollNowAsync();
        await Controls.RefreshAsync();
    }

    public void OnWindowHidden() => _poller.SetInterval(HiddenInterval);

    private void UpdateTooltip() => TrayTooltip = TooltipText.Format(_poller.Latest, Controls.ActiveProfileLabel);
}
