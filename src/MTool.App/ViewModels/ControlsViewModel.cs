using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Resources;
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
/// The keyboard backlight bar writes once per key press, click or finished drag, never the step the EC
/// already holds, and follows the Fn key through <see cref="FollowKeyboardBacklightAsync"/>.
/// </summary>
public sealed partial class ControlsViewModel : ObservableObject
{
    private readonly ProfileService _service;
    private readonly IP65Control _control;
    private readonly StatusViewModel _status;
    private ControlState? _shown;
    private bool _tableImplausible; // a draft record's map may read nonsense
    private byte _performanceRaw;
    private bool _portStateKnown;
    private int _refreshes;
    private KeyboardBacklightState? _backlight; // what the EC holds; null before the first read
    private bool _showingBacklight; // the EC, not the user, is moving the bar
    private bool _draggingBacklight;
    private int? _pendingBacklight; // chosen while a command ran; written when it is done
    private int _backlightShows; // a follow read started before the last show is stale

    public ControlsViewModel(ProfileService service, IP65Control control, StatusViewModel status, IUiDispatcher ui)
    {
        (_service, _control, _status) = (service, control, status);
        ProfileNames = NamesOf(service.Catalog);
        ChargeLimitDraft = ChargeLimitMax;
        var capabilities = control.Access.Capabilities;
        (ShowGpu, ShowFanProfiles, ShowCoolerBoost, ShowChargeLimit, ShowKeyboardBacklight) =
            (capabilities.GpuFan, capabilities.FanCurve, capabilities.CoolerBoost, capabilities.ChargeLimit, capabilities.KeyboardBacklight);
        PerformanceOptions = Array.AsReadOnly([.. capabilities.PerformanceModes.Select(m => new PerformanceOption(m, Texts.Mode(m)))]);
        service.CatalogChanged += () => ui.Post(OnCatalogChanged);
    }

    /// <summary>The built-in profiles first (<see cref="ProfileCatalog.BuiltIn"/>), then the custom ones.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> ProfileNames { get; private set; }

    // What the device record has (fixed for the session). A missing control is hidden; a locked one
    // stays visible and disabled.
    public bool ShowGpu { get; }

    public bool ShowFanProfiles { get; }

    public bool ShowCoolerBoost { get; }

    public bool ShowChargeLimit { get; }

    public bool ShowKeyboardBacklight { get; }

    public bool ShowPerformance => PerformanceOptions.Count > 0;

    /// <summary>The performance buttons, in the record's order.</summary>
    public IReadOnlyList<PerformanceOption> PerformanceOptions { get; }

    public int ChargeLimitMin => EcWriteRules.MinChargeLimitPercent;

    public int ChargeLimitMax => EcWriteRules.MaxChargeLimitPercent;

    /// <summary>The profile whose tables the EC holds; the desired one when several have the same tables.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProfileLabel))]
    public partial string? ActiveProfile { get; private set; }

    public string ActiveProfileLabel => ActiveProfile ?? (_tableImplausible ? Strings.Controls_FanTableImplausible : Strings.Controls_FanProfileUnrecognized);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PerformanceLabel))]
    public partial PerformanceMode? ActivePerformance { get; private set; }

    public string PerformanceLabel =>
        ActivePerformance is { } mode ? Texts.Mode(mode) : string.Format(Strings.Controls_PerformanceFactory, _performanceRaw);

    /// <summary>Null when the port state is not known.</summary>
    [ObservableProperty]
    public partial bool? CoolerBoostOn { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeLimitLabel))]
    public partial int? ChargeLimitActual { get; private set; }

    public string ChargeLimitLabel =>
        !_portStateKnown ? Strings.Charge_Unknown : ChargeLimitActual is { } percent ? string.Format(Strings.Format_Percent, percent) : Strings.Charge_Off;

    /// <summary>Slider value; written only by <see cref="ApplyChargeLimitCommand"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeLimitDraftLabel))]
    public partial int ChargeLimitDraft { get; set; }

    public string ChargeLimitDraftLabel => string.Format(Strings.Format_Percent, ChargeLimitDraft);

    public int KeyboardBacklightMax => ModeCodes.MaxKeyboardBacklightLevel;

    /// <summary>The bar: the EC's step, or the user's choice until the write's read-back replaces it.</summary>
    [ObservableProperty]
    public partial int KeyboardBacklightLevel { get; set; }

    /// <summary>False before the first read and for a byte that is none of the steps: no dot is selected.</summary>
    [ObservableProperty]
    public partial bool KeyboardBacklightKnown { get; private set; }

    public string KeyboardBacklightLabel => _backlight switch
    {
        null => "",
        { Level: 0 } => Strings.KeyboardBacklight_Off,
        { Level: { } level } => level.ToString(CultureInfo.CurrentCulture),
        { Raw: var raw } => string.Format(Strings.KeyboardBacklight_Unknown, raw),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(CanWritePort))]
    [NotifyCanExecuteChangedFor(nameof(SelectProfileCommand), nameof(SetPerformanceCommand), nameof(SetCoolerBoostCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyChargeLimitCommand), nameof(ReapplyCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(CanWritePort), nameof(CanChangeKeyboardBacklight), nameof(CanMoveChargeSlider))]
    [NotifyCanExecuteChangedFor(nameof(SelectProfileCommand), nameof(SetPerformanceCommand), nameof(SetCoolerBoostCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyChargeLimitCommand), nameof(ReapplyCommand))]
    public partial WriteMode WriteMode { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWritePort), nameof(CanMoveChargeSlider))]
    [NotifyCanExecuteChangedFor(nameof(SetCoolerBoostCommand), nameof(ApplyChargeLimitCommand))]
    public partial bool PortFeaturesAvailable { get; private set; } = true;

    /// <summary>
    /// The charge slider only moves the draft (Apply writes it), so a running command does not
    /// disable it: greying it out for every short write made the window flicker.
    /// </summary>
    public bool CanMoveChargeSlider => WriteMode != WriteMode.Locked && PortFeaturesAvailable;

    public bool CanWrite => WriteMode != WriteMode.Locked && !IsBusy;

    /// <summary>
    /// Only a lock disables the bar: disabled while a write runs, it would lose the keyboard focus.
    /// A step chosen during a command waits for it instead.
    /// </summary>
    public bool CanChangeKeyboardBacklight => WriteMode != WriteMode.Locked;

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
                _status.ShowWarning(string.Format(Strings.Controls_ReadFailed, ex.Message));
            }

            return;
        }

        if (refresh != _refreshes)
        {
            return;
        }

        // The cache now, never older than this read's: a read from before sleep cannot bring back the old Cooler Boost.
        state = state with { Port = _control.CachedPortState };
        Show(state);
        if (updateDrift)
        {
            ShowDrift(state);
        }

        WarnIfPortUnread(state, portUse, access);
    }

    /// <summary>
    /// A port read that failed is only logged and gives an unknown port state, which is no drift; without this the
    /// charge row would turn "unknown" in silence. Start-up and resume do not read the port, so they say nothing.
    /// </summary>
    private void WarnIfPortUnread(ControlState state, PortUse portUse, DeviceAccess access)
    {
        // A write failure message ("locked, restart") matters more than this one.
        if (portUse == PortUse.Allowed && access.PortFeaturesAvailable && state.Port is null && _status.MessageKind != MessageKind.Error)
        {
            _status.ShowWarning(Strings.Controls_PortUnread);
        }
    }

    /// <summary>
    /// Call after the wake reset of the port cache. No EC read (the EC is not reached while it wakes): the EC
    /// turns Cooler Boost off in sleep, so the shown one becomes unknown at once instead of at the resume
    /// reapply's refresh. The charge limit stays (the EC keeps it). A refresh still running shows the cache as
    /// it is when it ends, so one from before sleep cannot bring the old value back and one after wake is kept.
    /// </summary>
    public void ShowPortCacheAfterWake()
    {
        if (_shown is { } state)
        {
            Show(state with { Port = _control.CachedPortState });
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
        // Nothing is ever written with a draft record, so its drift would never go away. A verified
        // record locked for another reason (a failed write) still shows it.
        if (_control.Access.ExperimentalRecord is not null)
        {
            _status.SetDrift(new StateDrift(false, false, false, false), dryRun: false);
            return;
        }

        // A part the device lacks is never written (P65Control.ApplyDesiredAsync), so it is no drift either.
        var (desired, catalog) = _service.DesiredWithCatalog;
        var supported = _control.Access.Capabilities.Restrict(desired);
        _status.SetDrift(supported.DriftFrom(state, catalog), dryRun: WriteMode == WriteMode.DryRun);
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

    /// <summary>A press on the bar: until <see cref="EndKeyboardBacklightDrag"/>, moving it writes nothing.</summary>
    public void BeginKeyboardBacklightDrag() => _draggingBacklight = true;

    /// <summary>
    /// The release that ends a click or a drag: writes the step the bar ends on. A second release
    /// (the lost capture after the button) finds that step written or waiting and writes nothing.
    /// </summary>
    public void EndKeyboardBacklightDrag()
    {
        _draggingBacklight = false;
        _ = WriteKeyboardBacklightAsync(KeyboardBacklightLevel);
    }

    /// <summary>The window was hidden mid-press: no release will come, so the press ends without a write.</summary>
    public void CancelKeyboardBacklightDrag()
    {
        _draggingBacklight = false;
        ShowBacklight(_backlight);
    }

    /// <summary>
    /// Reads only the backlight, to follow the Fn key while the window is open. Skipped while a
    /// command runs, a step waits or the bar is held; a read that a newer EC state overtook is dropped.
    /// </summary>
    public async Task FollowKeyboardBacklightAsync()
    {
        if (!ShowKeyboardBacklight || IsBusy || _pendingBacklight is not null || _draggingBacklight)
        {
            return;
        }

        var shownBefore = _backlightShows;
        KeyboardBacklightState? state;
        try
        {
            state = await _control.ReadKeyboardBacklightAsync();
        }
        catch (Exception)
        {
            // A missed round, as in a sensor poll (which logs its misses): the next round reads again.
            return;
        }

        if (shownBefore == _backlightShows && !IsBusy && _pendingBacklight is null && !_draggingBacklight)
        {
            ShowBacklight(state);
        }
    }

    partial void OnKeyboardBacklightLevelChanged(int value)
    {
        if (!_showingBacklight && !_draggingBacklight)
        {
            _ = WriteKeyboardBacklightAsync(value);
        }
    }

    /// <summary>
    /// Not a command: the bar is a slider. While a command runs the step waits (the last one wins);
    /// when locked, or when the EC already holds it, the bar snaps back to the EC's step.
    /// </summary>
    private async Task WriteKeyboardBacklightAsync(int level)
    {
        if (!CanChangeKeyboardBacklight)
        {
            _pendingBacklight = null;
            ShowBacklight(_backlight);
            return;
        }

        if (IsBusy)
        {
            _pendingBacklight = level;
            return;
        }

        if (_backlight?.Level == level)
        {
            ShowBacklight(_backlight);
            return;
        }

        try
        {
            await RunAsync(PortUse.None, () => _service.SetKeyboardBacklightAsync(level));
        }
        catch (Exception ex)
        {
            // Started from a property change, not a command: nothing else would see the error.
            _status.ShowWarning(ex.Message);
        }
    }

    /// <summary>Runs after every command: the step chosen while it ran.</summary>
    private void WritePendingBacklight()
    {
        if (_pendingBacklight is { } level)
        {
            _pendingBacklight = null;
            _ = WriteKeyboardBacklightAsync(level);
        }
    }

    private void ShowBacklight(KeyboardBacklightState? state)
    {
        _backlight = state;
        _backlightShows++;
        KeyboardBacklightKnown = state?.Level is not null;
        OnPropertyChanged(nameof(KeyboardBacklightLabel));
        // A held or waiting step stays on the bar until it is written.
        if (_draggingBacklight || _pendingBacklight is not null || state?.Level is not { } level)
        {
            return;
        }

        _showingBacklight = true;
        try
        {
            KeyboardBacklightLevel = level;
        }
        finally
        {
            _showingBacklight = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task ReapplyAsync() => RunAsync(PortUse.Allowed, async () =>
    {
        var outcomes = await _service.ReapplyAsync(PortUse.Allowed);
        return new CommandResult(ReapplySummary.Worst(outcomes) ?? new WriteOutcome(WriteStatus.Rejected, [], Strings.Controls_NothingToReapply));
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

        WritePendingBacklight();
    }

    private void Show(ControlState state)
    {
        _shown = state;
        _tableImplausible = !state.FanCurves.Cpu.IsPlausible() || !state.FanCurves.Gpu.IsPlausible();
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

        ShowBacklight(state.KeyboardBacklight);
    }
}

/// <summary>One performance button: the mode it writes and its label.</summary>
public sealed record PerformanceOption(PerformanceMode Mode, string Name);
