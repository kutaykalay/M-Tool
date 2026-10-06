using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

/// <summary>
/// Profile buttons, Cooler Boost, performance mode and charge limit. Shows what the EC holds, not
/// what was clicked: after every command the EC is read back, so a refused write snaps back.
/// Cooler Boost and the charge limit come from the port cache (<see cref="ControlState.Port"/>):
/// null there is shown as unknown, and without a port their commands are disabled.
/// The profile list follows <see cref="ProfileService.Catalog"/>: a change there relabels the last EC state.
/// </summary>
public sealed partial class ControlsViewModel : ObservableObject
{
    private readonly ProfileService _service;
    private readonly IP65Control _control;
    private readonly StatusViewModel _status;
    private ControlState? _shown;
    private byte _performanceRaw;
    private bool _portStateKnown;
    private int _refreshes;

    public ControlsViewModel(ProfileService service, IP65Control control, StatusViewModel status, IUiDispatcher ui)
    {
        (_service, _control, _status) = (service, control, status);
        ProfileNames = NamesOf(service.Catalog);
        ChargeLimitDraft = ChargeLimitMax;
        service.CatalogChanged += () => ui.Post(OnCatalogChanged);
    }

    /// <summary>The built-in profiles first (<see cref="ProfileCatalog.BuiltIn"/>), then the custom ones.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> ProfileNames { get; private set; }

    public int ChargeLimitMin => EcWriteRules.MinChargeLimitPercent;

    public int ChargeLimitMax => EcWriteRules.MaxChargeLimitPercent;

    /// <summary>The profile whose tables the EC holds; the desired one when several have the same tables.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProfileLabel))]
    public partial string? ActiveProfile { get; private set; }

    public string ActiveProfileLabel => ActiveProfile ?? "Tanınmayan fan ayarı";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PerformanceLabel))]
    public partial PerformanceMode? ActivePerformance { get; private set; }

    public string PerformanceLabel =>
        ActivePerformance is { } mode ? ModeNames.Of(mode) : $"fabrika ayarı (0x{_performanceRaw:X2})";

    /// <summary>Null when the port state is not known.</summary>
    [ObservableProperty]
    public partial bool? CoolerBoostOn { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeLimitLabel))]
    public partial int? ChargeLimitActual { get; private set; }

    public string ChargeLimitLabel =>
        !_portStateKnown ? "bilinmiyor" : ChargeLimitActual is { } percent ? $"%{percent}" : "kapalı";

    /// <summary>Slider value; written only by <see cref="ApplyChargeLimitCommand"/>.</summary>
    [ObservableProperty]
    public partial int ChargeLimitDraft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(CanWritePort))]
    [NotifyCanExecuteChangedFor(nameof(SelectProfileCommand), nameof(SetPerformanceCommand), nameof(SetCoolerBoostCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyChargeLimitCommand), nameof(ReapplyCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(CanWritePort))]
    [NotifyCanExecuteChangedFor(nameof(SelectProfileCommand), nameof(SetPerformanceCommand), nameof(SetCoolerBoostCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyChargeLimitCommand), nameof(ReapplyCommand))]
    public partial WriteMode WriteMode { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWritePort))]
    [NotifyCanExecuteChangedFor(nameof(SetCoolerBoostCommand), nameof(ApplyChargeLimitCommand))]
    public partial bool PortFeaturesAvailable { get; private set; } = true;

    public bool CanWrite => WriteMode != WriteMode.Locked && !IsBusy;

    /// <summary>Cooler Boost and the charge limit: also need the raw port.</summary>
    public bool CanWritePort => CanWrite && PortFeaturesAvailable;

    /// <summary>
    /// Reads the EC and updates every control and the access band. The drift band is left alone
    /// while a command runs: the EC is between states then, and the command refreshes it when done.
    /// </summary>
    /// <param name="portUse">
    /// <see cref="PortUse.None"/> at start-up and after an automatic reapply: Cooler Boost and the
    /// charge limit then come from the cache, or stay unknown until the window is opened.
    /// </param>
    public Task RefreshAsync(PortUse portUse) => RefreshAsync(updateDrift: !IsBusy, portUse);

    private async Task RefreshAsync(bool updateDrift, PortUse portUse)
    {
        // Refreshes can overlap (window shown, command done, automatic reapply); only the newest is shown.
        var refresh = ++_refreshes;
        var access = _control.Access;
        WriteMode = access.WriteMode;
        PortFeaturesAvailable = access.PortFeaturesAvailable;
        _status.SetAccess(access);

        ControlState state;
        try
        {
            state = await _control.ReadControlStateAsync(portUse);
        }
        catch (Exception ex)
        {
            // A write failure message ("locked, restart") matters more than this one.
            if (_status.MessageKind != MessageKind.Error)
            {
                _status.ShowWarning($"Güncel ayarlar okunamadı, gösterilen değerler eski olabilir. ({ex.Message})");
            }

            return;
        }

        if (refresh != _refreshes)
        {
            return;
        }

        Show(state);
        if (updateDrift)
        {
            ShowDrift(state);
        }
    }

    /// <summary>No EC read: the profiles changed, the EC did not. A running command shows the drift when it is done.</summary>
    private void OnCatalogChanged()
    {
        ProfileNames = NamesOf(_service.Catalog);
        if (_shown is not { } state)
        {
            return;
        }

        ActiveProfile = MatchedProfileName(state);
        if (!IsBusy)
        {
            ShowDrift(state);
        }
    }

    private void ShowDrift(ControlState state)
    {
        var (desired, catalog) = _service.DesiredWithCatalog;
        _status.SetDrift(desired.DriftFrom(state, catalog), dryRun: WriteMode == WriteMode.DryRun);
    }

    // With equal tables (an unchanged copy) the label names the profile the user asked for.
    private string? MatchedProfileName(ControlState state)
    {
        var (desired, catalog) = _service.DesiredWithCatalog;
        return catalog.Match(state.FanCurves, desired.FanProfile)?.Name;
    }

    private static string[] NamesOf(ProfileCatalog catalog) => [.. catalog.Profiles.Select(p => p.Name)];

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SelectProfileAsync(string name) => RunAsync(PortUse.None, () => _service.SelectProfileAsync(name));

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SetPerformanceAsync(PerformanceMode mode) => RunAsync(PortUse.None, () => _service.SetPerformanceAsync(mode));

    [RelayCommand(CanExecute = nameof(CanWritePort))]
    private Task SetCoolerBoostAsync(bool on) => RunAsync(PortUse.Allowed, () => _service.SetCoolerBoostAsync(on));

    [RelayCommand(CanExecute = nameof(CanWritePort))]
    private Task ApplyChargeLimitAsync() => RunAsync(PortUse.Allowed, () => _service.SetChargeLimitAsync(ChargeLimitDraft));

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task ReapplyAsync() => RunAsync(PortUse.Allowed, async () =>
    {
        var outcomes = await _service.ReapplyAsync(PortUse.Allowed);
        return new CommandResult(ReapplySummary.Worst(outcomes) ?? new WriteOutcome(WriteStatus.Rejected, [], "Yeniden uygulanacak bir şey yok."));
    });

    /// <summary>
    /// One command at a time: the tray menu can start one while the window's is running. The
    /// refresh after it may use the port only when the command did (<paramref name="portUse"/>):
    /// a fan or performance change must not bring on a port read the user did not ask for.
    /// </summary>
    private async Task RunAsync(PortUse portUse, Func<Task<CommandResult>> command)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _status.Report(await command());
            await RefreshAsync(updateDrift: true, portUse);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(ControlState state)
    {
        _shown = state;
        _performanceRaw = state.PerformanceRaw;
        ActiveProfile = MatchedProfileName(state);
        ActivePerformance = state.Performance;
        OnPropertyChanged(nameof(PerformanceLabel));
        _portStateKnown = state.Port is not null;
        CoolerBoostOn = state.Port?.CoolerBoostOn;
        ChargeLimitActual = state.Port?.ChargeLimitPercent;
        OnPropertyChanged(nameof(ChargeLimitLabel));
        if (state.Port?.ChargeLimitPercent is { } percent)
        {
            ChargeLimitDraft = percent;
        }
    }
}
