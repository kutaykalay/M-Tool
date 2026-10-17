using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Core.Sensors;

namespace MTool.App.ViewModels;

/// <summary>
/// The experimental live values of a WMI2 laptop, in the reports-only window. Nothing is opened or
/// read before the user answers yes (asked each time reading is turned on, no is the default). Only
/// reads: firmware once, status on Show and Refresh, and while the window is shown the sensors alone
/// every <see cref="PollInterval"/>. Every round, timed or asked for, goes through one 2 s rule
/// (<see cref="Wmi2ReadPolicy.MinInterval"/>), the access gate and the breaker; rounds never overlap.
/// Once the breaker is open nothing is read and the timer stops until the user tries again.
/// Whether reading has stopped is read from the breaker itself, which other readers may share.
/// Every value is shown with its raw byte and marked not verified; messages never hold error text.
/// </summary>
/// <param name="open">Opens the interface (blocking, run off the UI thread); null when the laptop has no WMI2.</param>
/// <param name="accessGate">False while sleep closes EC access: nothing is opened or read.</param>
internal sealed partial class Wmi2LiveViewModel(
    Func<IWmi2LiveSource?> open,
    Wmi2CircuitBreaker breaker,
    IConfirm confirm,
    Func<bool> accessGate,
    TimeProvider time,
    IAppLog log) : ObservableObject, IDisposable
{
    /// <summary>
    /// A little over the 2 s rule, so a tick that comes a few milliseconds early on a busy UI thread
    /// is not skipped by it.
    /// </summary>
    internal static readonly TimeSpan PollInterval = Wmi2ReadPolicy.MinInterval + TimeSpan.FromMilliseconds(250);

    private const string Missing = "—";
    private const string Separator = " · ";

    private readonly CancellationTokenSource _closing = new();
    private IWmi2LiveSource? _source;
    private long? _lastRound; // a monotonic timestamp: a clock change must not shorten the 2 s rule
    private bool _hasFirmware;
    // All state below is touched on the UI thread only: the commands, the window events and the timer loop
    // (started there, its awaits resume there). Keep it so: none of it is locked.
    private PeriodicTimer? _timer;
    private bool _windowShown;

    private enum RoundKind
    {
        /// <summary>The first round: firmware, status and sensors.</summary>
        Full,

        /// <summary>Refresh and Try again: status and sensors.</summary>
        StatusAndSensors,

        /// <summary>A timer tick: the sensors only.</summary>
        Sensors,
    }

    [ObservableProperty]
    public partial string Firmware { get; private set; } = Missing;

    [ObservableProperty]
    public partial string CpuTemperature { get; private set; } = Missing;

    [ObservableProperty]
    public partial string GpuTemperature { get; private set; } = Missing;

    [ObservableProperty]
    public partial string CpuFan { get; private set; } = Missing;

    [ObservableProperty]
    public partial string GpuFan { get; private set; } = Missing;

    [ObservableProperty]
    public partial string PerformanceMode { get; private set; } = Missing;

    [ObservableProperty]
    public partial string FanMode { get; private set; } = Missing;

    [ObservableProperty]
    public partial string ChargeLimit { get; private set; } = Missing;

    [ObservableProperty]
    public partial string CoolerBoost { get; private set; } = Missing;

    /// <summary>Null while all is well; otherwise why the values are missing or may be old.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessageKind))]
    public partial string? Message { get; private set; }

    /// <summary>A pause (sleep, wake-up) is information; anything that stops or fails the reading is a warning.</summary>
    public MessageKind MessageKind => Message switch
    {
        null => MessageKind.None,
        var text when text == Strings.Sensors_Paused => MessageKind.Info,
        _ => MessageKind.Warning,
    };

    /// <summary>The user said yes and the interface is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsStarted { get; private set; }

    /// <summary>The breaker is open: nothing is read until <see cref="TryAgainCommand"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(TryAgainCommand))]
    public partial bool IsStopped { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(TryAgainCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>The interface could not be opened; it is not tried again in this session.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    public partial bool IsUnavailable { get; private set; }

    /// <summary>The timer runs: started, not stopped, and the window is shown.</summary>
    [ObservableProperty]
    public partial bool IsPolling { get; private set; }

    /// <summary>
    /// The section for a laptop whose probe found <paramref name="detected"/>: only a WMI2 laptop gets
    /// one, with a breaker of its own. Nothing is opened or read until the user asks.
    /// </summary>
    public static Wmi2LiveViewModel? For(
        WmiInterface? detected, Func<IWmi2LiveSource?> open, IConfirm confirm, Func<bool> accessGate, IAppLog log) =>
        detected == WmiInterface.Wmi2
            ? new Wmi2LiveViewModel(open, new Wmi2CircuitBreaker(), confirm, accessGate, TimeProvider.System, log)
            : null;

    /// <summary>The window tells whether it is on screen (shown and not minimised); only then does the timer run.</summary>
    public void SetWindowShown(bool shown)
    {
        _windowShown = shown;
        if (shown)
        {
            StartPolling();
        }
        else
        {
            StopPolling();
        }
    }

    /// <summary>Stops the timer and a running round (it does not count as a failure), then closes the interface.</summary>
    public void Dispose()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        _closing.Cancel(); // not disposed: a round still running reads its token
        StopPolling();
        _source?.Dispose();
        _source = null;
    }

    [RelayCommand(CanExecute = nameof(CanShow))]
    private async Task ShowAsync()
    {
        // No is the default answer: Enter on this question must not start reading.
        if (!confirm.Ask(Strings.Wmi2_Confirm, defaultYes: false))
        {
            log.Info("WMI2 live read: not confirmed, nothing was read.");
            return;
        }

        if (!accessGate())
        {
            Message = Strings.Sensors_Paused;
            return;
        }

        IsBusy = true;
        IWmi2LiveSource? source;
        try
        {
            source = await Task.Run(Open);
        }
        finally
        {
            IsBusy = false;
        }

        if (_closing.IsCancellationRequested)
        {
            source?.Dispose(); // the window closed while the interface was opening
            return;
        }

        _source = source;
        if (_source is null)
        {
            IsUnavailable = true;
            Message = Strings.Wmi2_Unavailable;
            return;
        }

        IsStarted = true;
        log.Info("WMI2 live read: started.");
        await WaitForMinIntervalAsync(); // Stop then Show within 2 s: read once the 2 s have passed
        await ReadAsync(RoundKind.Full);
        StartPolling();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync() => await ReadAsync(AskedRound);

    [RelayCommand(CanExecute = nameof(CanTryAgain))]
    private async Task TryAgainAsync()
    {
        if (TooSoon())
        {
            return;
        }

        breaker.Reset();
        IsStopped = false;
        log.Info("WMI2 live read: started again by the user.");
        await ReadAsync(AskedRound);
        StartPolling();
    }

    /// <summary>Turns reading off: the timer stops and the interface closes; Show asks again.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        StopPolling();
        _source?.Dispose();
        _source = null;
        IsStarted = false;
        IsStopped = false;
        _hasFirmware = false;
        Message = null;
        Firmware = CpuTemperature = GpuTemperature = CpuFan = GpuFan = Missing;
        PerformanceMode = FanMode = ChargeLimit = CoolerBoost = Missing;
        log.Info("WMI2 live read: stopped by the user.");
    }

    /// <summary>Refresh and Try again; with the firmware too while the first round has not read it (skipped by the gate or the 2 s rule).</summary>
    private RoundKind AskedRound => _hasFirmware ? RoundKind.StatusAndSensors : RoundKind.Full;

    private bool CanShow() => !IsStarted && !IsBusy && !IsUnavailable;

    private bool CanRefresh() => IsStarted && !IsBusy && !IsStopped;

    private bool CanTryAgain() => IsStarted && !IsBusy && IsStopped;

    private bool CanStop() => IsStarted && !IsBusy;

    private void StartPolling()
    {
        if (_timer is not null || !_windowShown || !IsStarted || IsStopped || _closing.IsCancellationRequested)
        {
            return;
        }

        _timer = new PeriodicTimer(PollInterval, time);
        IsPolling = true;
        _ = PollAsync(_timer);
    }

    private void StopPolling()
    {
        _timer?.Dispose(); // the loop's next wait returns false
        _timer = null;
        IsPolling = false;
    }

    private async Task PollAsync(PeriodicTimer timer)
    {
        try
        {
            // A tick already taken when the timer was stopped (window hidden, Stop, breaker) or replaced reads nothing.
            while (await timer.WaitForNextTickAsync() && ReferenceEquals(_timer, timer))
            {
                await ReadAsync(RoundKind.Sensors);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error("WMI2 live read: the timer stopped", ex);
            if (ReferenceEquals(_timer, timer))
            {
                StopPolling(); // so showing the window again can start a new one
            }
        }
    }

    private IWmi2LiveSource? Open()
    {
        try
        {
            var source = open();
            if (source is null)
            {
                log.Warn("WMI2 live read: the laptop has no WMI2 after all, nothing was read.");
            }

            return source;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"WMI2 live read: the interface could not be opened, nothing was read: {ex.ForLog()}");
            return null;
        }
    }

    /// <summary>One round; skipped while another runs or within the 2 s rule.</summary>
    private async Task ReadAsync(RoundKind kind)
    {
        if (_source is not { } source || IsBusy || TooSoon())
        {
            return;
        }

        if (!accessGate())
        {
            Message = Strings.Sensors_Paused;
            return;
        }

        _lastRound = time.GetTimestamp();
        IsBusy = true;
        try
        {
            var round = await breaker.RunAsync(ct => ReadRoundAsync(source, kind, ct), _closing.Token);
            Show(round);
            Message = null;
        }
        catch (Exception) when (_closing.IsCancellationRequested)
        {
            // The window closed (a disposed worker throws too): nothing more to show, not a failure.
        }
        catch (Wmi2StoppedException)
        {
            Message = Strings.Wmi2_Stopped;
        }
        catch (EcAccessException ex) when (ex.IsAccessPaused)
        {
            Message = Strings.Sensors_Paused;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed(ex);
        }
        finally
        {
            IsBusy = false;
            IsStopped = breaker.IsOpen;
            if (IsStopped)
            {
                StopPolling();
            }
        }
    }

    /// <summary>Within <see cref="Wmi2ReadPolicy.MinInterval"/> of the last round: every round, timed, refresh or retry, waits for it.</summary>
    private bool TooSoon() => _lastRound is { } last && time.GetElapsedTime(last) < Wmi2ReadPolicy.MinInterval;

    private async Task WaitForMinIntervalAsync()
    {
        if (_lastRound is not { } last || Wmi2ReadPolicy.MinInterval - time.GetElapsedTime(last) is not { Ticks: > 0 } left)
        {
            return;
        }

        try
        {
            await Task.Delay(left, time, _closing.Token);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed while waiting; ReadAsync then reads nothing.
        }
    }

    private void Failed(Exception error)
    {
        if (breaker.IsOpen)
        {
            log.Warn($"WMI2 live read: breaker open ({Wmi2CircuitBreaker.MaxFailuresInARow} failures): {breaker.LastError}");
            Message = Strings.Wmi2_Stopped;
            return;
        }

        log.Warn($"WMI2 live read failed: {error.ForLog()}");
        Message = Strings.Wmi2_ReadFailed;
    }

    private static async Task<Round> ReadRoundAsync(IWmi2LiveSource source, RoundKind kind, CancellationToken cancellationToken)
    {
        var firmware = kind == RoundKind.Full ? await source.ReadFirmwareAsync(cancellationToken).ConfigureAwait(false) : null;
        cancellationToken.ThrowIfCancellationRequested();
        var status = kind == RoundKind.Sensors ? null : await source.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sensors = await source.ReadSensorsAsync(cancellationToken).ConfigureAwait(false);
        return new Round(kind == RoundKind.Full, firmware, status, sensors);
    }

    private void Show(Round round)
    {
        if (round.HasFirmware)
        {
            Firmware = round.Firmware ?? Missing;
            _hasFirmware = true;
        }

        var s = round.Sensors;
        CpuTemperature = Temperature(s.CpuTempC);
        GpuTemperature = Temperature(s.GpuTempC);
        CpuFan = Fan(s.CpuFanPercent, s.Tach?.CpuHigh, s.Tach?.CpuLow, s.CpuRpm);
        GpuFan = Fan(s.GpuFanPercent, s.Tach?.GpuHigh, s.Tach?.GpuLow, s.GpuRpm);
        if (round.Status is not { } status)
        {
            return;
        }

        PerformanceMode = Named(PerformanceName(status.Performance), status.PerformanceRaw);
        FanMode = Named(FanModeName(status.FanMode), status.FanModeRaw);
        ChargeLimit = Named(status.ChargeLimitPercent is { } percent ? Percent(percent) : null, status.ChargeRaw);
        CoolerBoost = Named(status.CoolerBoost ? Strings.Wmi2_On : Strings.Wmi2_Off, status.CoolerBoostRaw);
    }

    private static string Temperature(int? celsius) =>
        celsius is { } c ? string.Create(CultureInfo.InvariantCulture, $"{c} °C") : Missing;

    /// <summary>The percent and the speed; an unclear speed shows its raw bytes instead (<see cref="Wmi2Rpm"/>).</summary>
    private static string Fan(int percent, byte? high, byte? low, int rpm)
    {
        var speed = (high, low) is not ({ } h, { } l) ? Rpm(rpm)
            : Wmi2Rpm.Resolve(h, l) is { } resolved ? Rpm(resolved)
            : string.Format(Strings.Wmi2_Raw, $"{Hex(h)} {Hex(l)}");
        return Percent(percent) + Separator + speed;
    }

    private static string Rpm(int rpm) => string.Create(CultureInfo.InvariantCulture, $"{rpm} rpm");

    private static string Percent(int percent) => string.Create(CultureInfo.InvariantCulture, $"{percent}%");

    /// <summary>A known value with its raw byte, marked not verified; an unknown one as the raw byte alone.</summary>
    private static string Named(string? name, byte raw) =>
        name is null ? Hex(raw) : string.Format(Strings.Wmi2_Unverified, name, Hex(raw));

    private static string Hex(byte value) => string.Create(CultureInfo.InvariantCulture, $"0x{value:X2}");

    private static string? PerformanceName(Core.Device.PerformanceMode? mode) => mode switch
    {
        Core.Device.PerformanceMode.Balanced => Strings.Mode_Balanced,
        Core.Device.PerformanceMode.Eco => Strings.Mode_Eco,
        Core.Device.PerformanceMode.High => Strings.Mode_High,
        _ => null,
    };

    private static string? FanModeName(Wmi2FanMode? mode) => mode switch
    {
        Wmi2FanMode.Auto => Strings.Wmi2_FanAuto,
        Wmi2FanMode.Silent => Strings.Wmi2_FanSilent,
        Wmi2FanMode.Advanced => Strings.Wmi2_FanAdvanced,
        _ => null,
    };

    private sealed record Round(bool HasFirmware, string? Firmware, Wmi2Status? Status, SensorSnapshot Sensors);
}
