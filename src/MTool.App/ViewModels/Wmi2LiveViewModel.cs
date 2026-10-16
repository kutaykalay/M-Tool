using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Sensors;

namespace MTool.App.ViewModels;

/// <summary>
/// The experimental live values of a WMI2 laptop, in the reports-only window. Nothing is opened or
/// read before the user answers yes (asked once per session, no is the default). Only reads: firmware
/// once, then status and sensors on each round the user asks for (Show, Refresh, Try again), never more
/// often than <see cref="Wmi2ReadPolicy.MinInterval"/>. A periodic reader must read the sensors only.
/// A round runs through the breaker; once it is open nothing is read until the user tries again.
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
    private const string Missing = "—";
    private const string Separator = " · ";

    private readonly CancellationTokenSource _closing = new();
    private IWmi2LiveSource? _source;
    private DateTimeOffset? _lastRound;

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
    public partial string? Message { get; private set; }

    /// <summary>The user said yes and the interface is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
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
    public partial bool IsBusy { get; private set; }

    /// <summary>The interface could not be opened; it is not tried again in this session.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    public partial bool IsUnavailable { get; private set; }

    /// <summary>Stops a running round (it does not count as a failure) and closes the interface.</summary>
    public void Dispose()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        _closing.Cancel(); // not disposed: a round still running reads its token
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
        await ReadAsync(withFirmware: true);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync() => await ReadAsync(withFirmware: false);

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
        await ReadAsync(withFirmware: false);
    }

    private bool CanShow() => !IsStarted && !IsBusy && !IsUnavailable;

    private bool CanRefresh() => IsStarted && !IsBusy && !IsStopped;

    private bool CanTryAgain() => IsStarted && !IsBusy && IsStopped;

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
            log.Warn($"WMI2 live read: the interface could not be opened, nothing was read: {ex.Message}");
            return null;
        }
    }

    private async Task ReadAsync(bool withFirmware)
    {
        if (_source is not { } source || TooSoon())
        {
            return;
        }

        if (!accessGate())
        {
            Message = Strings.Sensors_Paused;
            return;
        }

        _lastRound = time.GetUtcNow();
        IsBusy = true;
        try
        {
            var round = await breaker.RunAsync(ct => ReadRoundAsync(source, withFirmware, ct), _closing.Token);
            Show(round);
            Message = null;
        }
        catch (Wmi2StoppedException)
        {
            Message = Strings.Wmi2_Stopped;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed: nothing more to show.
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
        }
    }

    /// <summary>Within <see cref="Wmi2ReadPolicy.MinInterval"/> of the last round: every round, refresh or retry, waits for it.</summary>
    private bool TooSoon() => _lastRound is { } last && time.GetUtcNow() - last < Wmi2ReadPolicy.MinInterval;

    private void Failed(Exception error)
    {
        if (breaker.IsOpen)
        {
            log.Warn($"WMI2 live read: breaker open ({Wmi2CircuitBreaker.MaxFailuresInARow} failures): {breaker.LastError}");
            Message = Strings.Wmi2_Stopped;
            return;
        }

        log.Warn($"WMI2 live read failed: {error.Message}");
        Message = Strings.Wmi2_ReadFailed;
    }

    private static async Task<Round> ReadRoundAsync(IWmi2LiveSource source, bool withFirmware, CancellationToken cancellationToken)
    {
        var firmware = withFirmware ? await source.ReadFirmwareAsync(cancellationToken).ConfigureAwait(false) : null;
        cancellationToken.ThrowIfCancellationRequested();
        var status = await source.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sensors = await source.ReadSensorsAsync(cancellationToken).ConfigureAwait(false);
        return new Round(withFirmware, firmware, status, sensors);
    }

    private void Show(Round round)
    {
        if (round.HasFirmware)
        {
            Firmware = round.Firmware ?? Missing;
        }

        var (status, s) = (round.Status, round.Sensors);
        CpuTemperature = Temperature(s.CpuTempC);
        GpuTemperature = Temperature(s.GpuTempC);
        CpuFan = Fan(s.CpuFanPercent, s.Tach?.CpuHigh, s.Tach?.CpuLow, s.CpuRpm);
        GpuFan = Fan(s.GpuFanPercent, s.Tach?.GpuHigh, s.Tach?.GpuLow, s.GpuRpm);
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

    private sealed record Round(bool HasFirmware, string? Firmware, Wmi2Status Status, SensorSnapshot Sensors);
}
