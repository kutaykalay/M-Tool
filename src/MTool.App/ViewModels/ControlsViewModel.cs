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
/// </summary>
public sealed partial class ControlsViewModel : ObservableObject
{
    private readonly ProfileService _service;
    private readonly IP65Control _control;
    private readonly ProfileCatalog _catalog;
    private readonly StatusViewModel _status;
    private byte _performanceRaw;
    private bool _portStateKnown;
    private int _refreshes;

    public ControlsViewModel(ProfileService service, IP65Control control, ProfileCatalog catalog, StatusViewModel status)
    {
        (_service, _control, _catalog, _status) = (service, control, catalog, status);
        ProfileNames = catalog.Profiles.Select(p => p.Name).ToArray();
        ChargeLimitDraft = ChargeLimitMax;
    }

    public IReadOnlyList<string> ProfileNames { get; }

    public int ChargeLimitMin => EcWriteRules.MinChargeLimitPercent;

    public int ChargeLimitMax => EcWriteRules.MaxChargeLimitPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProfileLabel))]
    public partial string? ActiveProfile { get; private set; }

    public string ActiveProfileLabel => ActiveProfile ?? "Özel/bilinmeyen tablo";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PerformanceLabel))]
    public partial PerformanceMode? ActivePerformance { get; private set; }

    public string PerformanceLabel => ActivePerformance switch
    {
        PerformanceMode.High => "Yüksek",
        PerformanceMode.Balanced => "Dengeli",
        PerformanceMode.Eco => "Pil",
        _ => $"tanımsız (fabrika, 0x{_performanceRaw:X2})",
    };

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
                _status.ShowWarning($"EC durumu okunamadı, gösterilen değerler eski olabilir: {ex.Message}");
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
            _status.SetDrift(_service.Desired.DriftFrom(state, _catalog), dryRun: access.WriteMode == WriteMode.DryRun);
        }
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SelectProfileAsync(string name) => RunAsync(() => _service.SelectProfileAsync(name));

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SetPerformanceAsync(PerformanceMode mode) => RunAsync(() => _service.SetPerformanceAsync(mode));

    [RelayCommand(CanExecute = nameof(CanWritePort))]
    private Task SetCoolerBoostAsync(bool on) => RunAsync(() => _service.SetCoolerBoostAsync(on));

    [RelayCommand(CanExecute = nameof(CanWritePort))]
    private Task ApplyChargeLimitAsync() => RunAsync(() => _service.SetChargeLimitAsync(ChargeLimitDraft));

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task ReapplyAsync() => RunAsync(async () =>
    {
        var outcomes = await _service.ReapplyAsync(PortUse.Allowed);
        return new CommandResult(ReapplySummary.Worst(outcomes) ?? new WriteOutcome(WriteStatus.Rejected, [], "Yeniden uygulanacak bir şey yok."));
    });

    /// <summary>One command at a time: the tray menu can start one while the window's is running.</summary>
    private async Task RunAsync(Func<Task<CommandResult>> command)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _status.Report(await command());
            await RefreshAsync(updateDrift: true, PortUse.Allowed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(ControlState state)
    {
        _performanceRaw = state.PerformanceRaw;
        ActiveProfile = _catalog.Match(state.FanCurves)?.Name;
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
