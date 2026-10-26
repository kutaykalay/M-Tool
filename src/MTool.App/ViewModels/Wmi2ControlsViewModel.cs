using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Hardware;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Core.Settings;

namespace MTool.App.ViewModels;

/// <summary>Where experimental control stands in the WMI2 window.</summary>
internal enum Wmi2ControlState
{
    /// <summary>Reading has not started or the firmware is not known yet: nothing to say.</summary>
    Hidden,

    /// <summary>The firmware is not an exact list entry (or there is no list): only reading, and why.</summary>
    NotOffered,

    /// <summary>An exact entry without consent for this firmware: the user may turn control on.</summary>
    Offered,

    /// <summary>Reading is being switched to control.</summary>
    Opening,

    /// <summary>The rounds read through control and the switches write through its gateway.</summary>
    Open,

    /// <summary>Control did not open; reading goes on, the reason is shown and the user may try again.</summary>
    Unavailable,
}

/// <summary>
/// Experimental control in the WMI2 window (CLAUDE.md, "WMI2 deneysel yazma"; plan asama15 15c-5). Offered only
/// on an exact list entry. The first time, and after a BIOS update, it is turned on only after a question that names
/// the risks and the way back (No is the default); the consent is kept for that firmware, so later sessions open
/// control by themselves once a round has shown it (Kutay, 2026-10-26). Opening writes nothing. Every write is the
/// user's own switch, goes through the gateway, and the write-path test (K5) runs before the first one. Three
/// writes the rules allow but that lose something MSI Center set ask first (No is the default). Nothing is asked
/// after a write. The live values view model owns the source; this one asks it to switch, and holds it while a
/// write runs or its question is open, so Stop and switching wait. No write while reading has stopped.
/// All state is touched on the UI thread only, like the live values view model.
/// </summary>
internal sealed partial class Wmi2ControlsViewModel : ObservableObject, IDisposable
{
    private const byte MsiCenterFourthMode = 0xC5;
    private const int LowestNewChargeLimit = 50;

    private readonly Wmi2LiveViewModel _live;
    private readonly Func<string, CancellationToken, Task<Wmi2ControlOpening>> _openControl;
    private readonly IConfirm _confirm;
    private readonly IWmi2Consent _consent;
    private readonly TimeProvider _time;
    private readonly IAppLog _log;
    private readonly WmiInterface? _detected;
    private readonly Wmi2FirmwareList? _list;
    private readonly string _preStatePath;
    private readonly CancellationTokenSource _closing = new();
    private bool _autoOpenTried; // control opens by itself at most once per reading, so a failure does not loop
    private bool _turnedOff; // the user turned control off in this session: it does not open by itself again

    /// <param name="openControl">Opens control for the consented firmware; it runs off the UI thread.</param>
    /// <param name="preStatePath">The "before M-Tool" snapshot file, named when it is the problem.</param>
    public Wmi2ControlsViewModel(
        Wmi2LiveViewModel live, Func<string, CancellationToken, Task<Wmi2ControlOpening>> openControl, IConfirm confirm,
        IWmi2Consent consent, TimeProvider time, IAppLog log, WmiInterface? detected, Wmi2FirmwareList? list, string preStatePath)
    {
        (_live, _openControl, _confirm, _consent, _time, _log) = (live, openControl, confirm, consent, time, log);
        (_detected, _list, _preStatePath) = (detected, list, preStatePath);
        _live.StateChanged += OnLiveChanged;
        _live.PropertyChanged += OnLivePropertyChanged;
    }

    [ObservableProperty]
    public partial Wmi2ControlState State { get; private set; } = Wmi2ControlState.Hidden;

    /// <summary>Why control is not offered, did not open, or cannot write in this session; null otherwise.</summary>
    [ObservableProperty]
    public partial string? Reason { get; private set; }

    /// <summary>What the last write did, or that the consent could not be saved; null before any.</summary>
    [ObservableProperty]
    public partial string? Result { get; private set; }

    [ObservableProperty]
    public partial MessageKind ResultKind { get; private set; }

    /// <summary>One line per register after "Back to before M-Tool"; empty otherwise.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> RevertLines { get; private set; } = [];

    /// <summary>A write runs, or its question is open.</summary>
    [ObservableProperty]
    public partial bool IsWriting { get; private set; }

    /// <summary>Reading is being switched to control or back.</summary>
    [ObservableProperty]
    public partial bool IsSwitching { get; private set; }

    [ObservableProperty]
    public partial bool? CoolerBoostOn { get; private set; }

    [ObservableProperty]
    public partial PerformanceMode? Performance { get; private set; }

    [ObservableProperty]
    public partial Wmi2FanMode? FanMode { get; private set; }

    [ObservableProperty]
    public partial int? ChargeLimitPercent { get; private set; }

    private IWmi2Control? Control => _live.Source as IWmi2Control;

    /// <summary>Nothing opens by itself afterwards; a write still running reads the cancelled token and is not shown.</summary>
    public void Dispose()
    {
        _live.StateChanged -= OnLiveChanged;
        _live.PropertyChanged -= OnLivePropertyChanged;
        _closing.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private async Task EnableAsync()
    {
        if (_live.FirmwareVersion is not { } firmware)
        {
            return;
        }

        if (_consent.Current?.Firmware != firmware)
        {
            // No is the default answer: Enter on this question must not turn control on.
            if (!_confirm.Ask(Strings.Wmi2_ControlConfirm, defaultYes: false))
            {
                _log.Info("WMI2 experimental control: not confirmed, nothing was opened.");
                return;
            }

            SaveConsent(new Wmi2ExperimentalConsent(firmware, _time.GetUtcNow()));
            _log.Info($"WMI2 experimental control: the user consented for {firmware}.");
        }

        _turnedOff = false;
        await OpenControlAsync(firmware);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SetCoolerBoostAsync(bool on) => ChangeAsync(new Wmi2Change.CoolerBoost(on), warning: null);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SetPerformanceAsync(PerformanceMode mode) =>
        ChangeAsync(new Wmi2Change.Performance(mode), _live.LastStatus?.PerformanceRaw == MsiCenterFourthMode ? Strings.Wmi2_WarnC5 : null);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SetFanModeAsync(Wmi2FanMode mode) =>
        ChangeAsync(new Wmi2Change.FanMode(mode), _live.LastStatus?.FanMode == Wmi2FanMode.Advanced ? Strings.Wmi2_WarnCustomCurve : null);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task SetChargeLimitAsync(int percent) =>
        ChangeAsync(
            new Wmi2Change.ChargeLimit(percent),
            _live.LastStatus?.ChargeLimitPercent is { } current and < LowestNewChargeLimit ? string.Format(Strings.Wmi2_WarnLowChargeLimit, current) : null);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task RevertAsync() =>
        RunWriteAsync("back to before M-Tool", warning: null, async control =>
        {
            var outcomes = await control.RevertAsync(_closing.Token);
            RevertLines = outcomes.Select(o => Wmi2OutcomeText.RevertLine(o, _preStatePath)).ToArray();
            ResultKind = outcomes.All(o => Wmi2OutcomeText.KindOf(o) == MessageKind.Info) ? MessageKind.Info : MessageKind.Warning;
            _log.Info($"WMI2 back to before M-Tool: {string.Join(", ", outcomes.Select(Describe))}.");
        });

    /// <summary>Goes back to reading only, then forgets the consent; nothing is written.</summary>
    [RelayCommand(CanExecute = nameof(CanTurnOff))]
    private async Task TurnOffAsync()
    {
        IsSwitching = true;
        bool switched;
        try
        {
            switched = await _live.SwitchSourceAsync(_ => Task.FromResult<IWmi2LiveSource?>(null));
        }
        finally
        {
            IsSwitching = false;
        }

        if (!switched)
        {
            // A round started meanwhile, or the window closed: control stays as it was, consent and all.
            _log.Info("WMI2 experimental control: turning off could not switch now; nothing changed.");
            (Result, ResultKind) = (Strings.Wmi2_ProblemUnreachable, MessageKind.Warning);
            Update(allowAutoOpen: false);
            return;
        }

        _turnedOff = true; // also when the file keeps the consent: this session does not open control again
        SaveConsent(null);
        _log.Info("WMI2 experimental control: turned off by the user.");
        Update(allowAutoOpen: false);
    }

    private bool CanEnable() => State is Wmi2ControlState.Offered or Wmi2ControlState.Unavailable && IsIdle();

    private bool CanWrite() => State == Wmi2ControlState.Open && IsIdle() && !_live.IsStopped && Control is { Layout.Verified: true };

    private bool CanTurnOff() => State is Wmi2ControlState.Open or Wmi2ControlState.Unavailable && IsIdle();

    /// <summary>No write, no switch, no round running: a round would make a switch refuse.</summary>
    private bool IsIdle() => !IsWriting && !IsSwitching && !_live.IsBusy;

    partial void OnStateChanged(Wmi2ControlState value) => RefreshCommands();

    partial void OnIsWritingChanged(bool value) => RefreshCommands();

    partial void OnIsSwitchingChanged(bool value) => RefreshCommands();

    private void RefreshCommands()
    {
        EnableCommand.NotifyCanExecuteChanged();
        SetCoolerBoostCommand.NotifyCanExecuteChanged();
        SetPerformanceCommand.NotifyCanExecuteChanged();
        SetFanModeCommand.NotifyCanExecuteChanged();
        SetChargeLimitCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        TurnOffCommand.NotifyCanExecuteChanged();
    }

    private void OnLiveChanged(object? sender, EventArgs e)
    {
        if (!IsSwitching)
        {
            Update(allowAutoOpen: true);
        }
    }

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Wmi2LiveViewModel.IsBusy) or nameof(Wmi2LiveViewModel.IsStopped))
        {
            RefreshCommands();
        }
    }

    /// <summary>What the live values say now: the switches, and where control stands.</summary>
    /// <param name="allowAutoOpen">False from inside a switch: control is never opened again from there, only after the next round.</param>
    private void Update(bool allowAutoOpen)
    {
        var status = _live.LastStatus;
        (CoolerBoostOn, Performance, FanMode, ChargeLimitPercent) =
            (status?.CoolerBoost, status?.Performance, status?.FanMode, status?.ChargeLimitPercent);

        if (Control is { } control)
        {
            (State, Reason) = (Wmi2ControlState.Open, control.Layout.Verified ? null : string.Format(Strings.Wmi2_ControlLayout, control.Layout));
            return;
        }

        if (!_live.IsStarted || _live.FirmwareVersion is not { } firmware)
        {
            (State, Reason, Result, RevertLines) = (Wmi2ControlState.Hidden, null, null, []);
            _autoOpenTried &= _live.IsStarted;
            return;
        }

        UpdateForFirmware(firmware, allowAutoOpen);
    }

    private void UpdateForFirmware(string firmware, bool allowAutoOpen)
    {
        var match = Wmi2Support.Match(_detected, _list, firmware);
        if (match != Wmi2ListMatch.Exact)
        {
            (State, Reason) = (Wmi2ControlState.NotOffered, match == Wmi2ListMatch.Unavailable ? Strings.Wmi2_ControlListUnavailable : Strings.Wmi2_ControlNotExact);
            return;
        }

        if (State == Wmi2ControlState.Unavailable)
        {
            return; // the reason stays until the user tries again or turns control off
        }

        var consented = _consent.Current?.Firmware;
        if (consented == firmware && allowAutoOpen && !_autoOpenTried && !_turnedOff && !_closing.IsCancellationRequested)
        {
            _autoOpenTried = true;
            _ = OpenControlAsync(firmware);
            return;
        }

        (State, Reason) = (Wmi2ControlState.Offered, consented is not null && consented != firmware ? Strings.Wmi2_ControlFirmwareChanged : null);
    }

    /// <summary>
    /// Asks the live values to switch to control; when it does not open, reading goes on and the reason is shown.
    /// Started without being awaited by the automatic opening, so it lets nothing escape.
    /// </summary>
    private async Task OpenControlAsync(string firmware)
    {
        (State, Reason, IsSwitching) = (Wmi2ControlState.Opening, null, true);
        Wmi2ControlOpening? opening = null;
        bool switched;
        try
        {
            switched = await _live.SwitchSourceAsync(async ct =>
            {
                opening = await Task.Run(() => _openControl(firmware, ct), ct);
                return opening.Source;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error("WMI2 experimental control: opening failed", ex);
            (State, Reason) = (Wmi2ControlState.Unavailable, Strings.Wmi2_ControlOpenFailed);
            return;
        }
        finally
        {
            IsSwitching = false;
        }

        Opened(switched, opening);
    }

    private void Opened(bool switched, Wmi2ControlOpening? opening)
    {
        if (!switched)
        {
            _autoOpenTried = false; // a round was running or the window closed: the next round may try again
            Update(allowAutoOpen: false);
            return;
        }

        if (Control is null && _live.IsStarted)
        {
            (State, Reason) = (Wmi2ControlState.Unavailable, opening?.Unavailable is { } reason ? Wmi2OutcomeText.Of(reason) : Strings.Wmi2_ControlOpenFailed);
            return;
        }

        Update(allowAutoOpen: false); // open, or reading did not come back either (hidden)
    }

    private Task ChangeAsync(Wmi2Change change, string? warning) =>
        RunWriteAsync(change.ToString(), warning, async control => Show(await control.ApplyAsync(change, _closing.Token)));

    /// <summary>
    /// Holds the source from the question on, so Stop and switching wait; checks after the question that the window
    /// is still open and control is the same; runs the write-path test first while it has not passed (a failed test
    /// is the result and nothing else runs); afterwards the next round reads the status.
    /// </summary>
    private async Task RunWriteAsync(string what, string? warning, Func<IWmi2Control, Task> write)
    {
        if (Control is not { } control)
        {
            return;
        }

        (IsWriting, _live.IsSourceHeld) = (true, true);
        try
        {
            // No is the default answer: these writes lose something only MSI Center can set again.
            if (warning is not null && !_confirm.Ask(warning, defaultYes: false))
            {
                _log.Info($"WMI2 write not confirmed: {what}.");
                return;
            }

            if (_closing.IsCancellationRequested || !ReferenceEquals(Control, control))
            {
                return; // the window closed while the question was open
            }

            (Result, RevertLines) = (null, []);
            if (!control.IsWritePathTested)
            {
                var test = await control.TestWritePathAsync(_closing.Token);
                if (test.Status is not (WriteStatus.Applied or WriteStatus.DryRun))
                {
                    Show(test);
                    return;
                }
            }

            await write(control);
        }
        catch (Exception) when (_closing.IsCancellationRequested)
        {
            // The window closed while writing: nothing more to show.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error($"WMI2 write failed: {what}", ex);
            (Result, ResultKind) = (Strings.Wmi2_ProblemWriteFailed, MessageKind.Warning);
        }
        finally
        {
            (IsWriting, _live.IsSourceHeld) = (false, false);
            _live.WantStatus();
        }
    }

    private void Show(Wmi2WriteOutcome outcome)
    {
        (Result, ResultKind) = (Wmi2OutcomeText.Of(outcome, _preStatePath), Wmi2OutcomeText.KindOf(outcome));
        _log.Info($"WMI2 write {Describe(outcome)}.");
    }

    private static string Describe(Wmi2WriteOutcome outcome) =>
        string.Join(' ', new object?[] { $"0x{outcome.Register:X2}", outcome.Status, outcome.Rejection, outcome.Block, outcome.Problem }.OfType<object>());

    /// <summary>A consent that cannot be saved still holds for this session; the user is told it will not last.</summary>
    private void SaveConsent(Wmi2ExperimentalConsent? consent)
    {
        try
        {
            _consent.Save(consent);
        }
        // Every way the save can fail, as for the other settings (ProfileService).
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or System.Security.SecurityException or JsonException)
        {
            _log.Warn($"WMI2 experimental control: the consent could not be saved: {ex.ForLog()}");
            (Result, ResultKind) = (Strings.Wmi2_ConsentNotSaved, MessageKind.Warning);
        }
    }
}
