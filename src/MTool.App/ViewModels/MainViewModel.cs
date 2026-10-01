using CommunityToolkit.Mvvm.ComponentModel;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Sensors;
using TooltipText = MTool.App.Tray.TrayTooltip;

namespace MTool.App.ViewModels;

/// <summary>Ties the window, the tray tooltip and the sensor poller together.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>1 s with the window open, 5 s for the tray tooltip.</summary>
    public static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(5);

    private readonly SensorPoller _poller;
    private string? _autoReapplyMessage;

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

    /// <summary>Does not read the port: start-up may be at sign-in, and the window reads it when opened.</summary>
    /// <param name="startupWarnings">Settings problems found while loading (shown once).</param>
    public async Task InitializeAsync(IReadOnlyList<string> startupWarnings)
    {
        if (startupWarnings.Count > 0)
        {
            Status.ShowWarning(string.Join(" ", startupWarnings));
        }

        await Controls.RefreshAsync(PortUse.None);
        UpdateTooltip();
    }

    public async Task OnWindowShownAsync()
    {
        _poller.SetInterval(VisibleInterval);
        _ = _poller.PollNowAsync();
        await Controls.RefreshAsync(PortUse.Allowed);
    }

    /// <summary>
    /// Shows the result of an automatic reapply (start-up, resume, retry) on the UI thread. Success
    /// is quiet and clears an earlier automatic warning; a rejection is a warning band; a failed
    /// write is the usual error band and balloon. The controls are reread without the port.
    /// </summary>
    public async Task OnAutoReappliedAsync(AutoReapplyResult result)
    {
        var worst = ReapplySummary.Worst(result.Outcomes);
        if (worst is null || worst.Status is WriteStatus.Applied or WriteStatus.DryRun)
        {
            if (_autoReapplyMessage is { } earlier)
            {
                Status.ClearMessage(ifShowing: earlier);
                _autoReapplyMessage = null;
            }
        }
        else
        {
            Status.Report(new CommandResult(worst with { Message = $"Otomatik yeniden uygulama ({TriggerName(result.Trigger)}): {worst.Message}" }));
            _autoReapplyMessage = Status.Message;
        }

        await Controls.RefreshAsync(PortUse.None);
    }

    public void OnWindowHidden() => _poller.SetInterval(HiddenInterval);

    private static string TriggerName(ReapplyTrigger trigger) => trigger switch
    {
        ReapplyTrigger.Startup => "açılış",
        ReapplyTrigger.Resume => "uyanış",
        _ => "yeniden deneme",
    };

    private void UpdateTooltip() => TrayTooltip = TooltipText.Format(_poller.Latest, Controls.ActiveProfileLabel);
}
