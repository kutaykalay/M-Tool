using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>The firmware is verified for writes and the port is wanted, but PawnIO is not installed.</summary>
internal sealed class PawnIoMissingException() : Exception(string.Format(Strings.PawnIo_Missing, PawnIoInstallation.InstallCommand))
{
    /// <summary>The same sentence in English, for the log.</summary>
    public string LogMessage { get; } = $"PawnIO is not installed. To install: {PawnIoInstallation.InstallCommand}";
}

/// <summary>How a session reaches the EC: MSI WMI with or without the raw port.</summary>
internal enum EcBackends
{
    /// <summary>
    /// WMI for everything it maps; the raw port (PawnIO) only for Cooler Boost and the charge limit,
    /// and only on a firmware verified for writes. Anywhere else the session falls back to WMI only.
    /// </summary>
    Hybrid,

    /// <summary>WMI only. PawnIO is never opened, so the port cannot be used at all.</summary>
    WmiOnly,
}

/// <summary>
/// Opens WMI, (in <see cref="EcBackends.Hybrid"/>) PawnIO, the Access_EC lock and the EC worker
/// over <see cref="RoutedEcRegisters"/>. The lock is held for WMI accesses too, so they queue
/// behind port accesses like every other EC operation.
/// </summary>
internal sealed class EcSession : IDisposable
{
    private const int FirmwareAttempts = 2;

    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FirmwareRetryDelay = TimeSpan.FromSeconds(1);

    private readonly PawnIoPortIo? _ports;
    private readonly AccessEcMutex _ecLock;
    private readonly IAppLog _log;
    private readonly EcTroubleLog? _troubleLog;

    private EcSession(
        PawnIoPortIo? ports, AccessEcMutex ecLock, EcController? controller, EcWorker worker, IReadOnlyList<DeviceConfig> catalog,
        DeviceLayout layout, IAppLog log, EcTroubleLog? troubleLog, IReadOnlyList<string> startupWarnings)
    {
        Catalog = catalog;
        Layout = layout;
        _ports = ports;
        _ecLock = ecLock;
        Controller = controller;
        Worker = worker;
        _log = log;
        _troubleLog = troubleLog;
        StartupWarnings = startupWarnings;
    }

    /// <summary>The raw port; null in <see cref="EcBackends.WmiOnly"/>.</summary>
    public EcController? Controller { get; }

    public EcWorker Worker { get; }

    /// <summary>How this model's registers are read (sensors, tables, firmware, dump): the matched record's.</summary>
    /// <remarks>An unmatched WMI1 model is read with the fallback record, read-only and without the port.</remarks>
    public DeviceLayout Layout { get; }

    /// <summary>For the window: what the user should know about how this session opened.</summary>
    public IReadOnlyList<string> StartupWarnings { get; }

    /// <summary>Every embedded device record that loaded.</summary>
    public IReadOnlyList<DeviceConfig> Catalog { get; }

    /// <summary>Derived from the port the router was given, so the gateway's policy cannot disagree with it.</summary>
    public bool PortAvailable => Controller is not null;

    /// <param name="accessGate">When it returns false, EC operations fail without touching the EC (sleep/resume).</param>
    /// <param name="backend">
    /// What the caller would like. <see cref="EcBackends.Hybrid"/> becomes WMI only unless the firmware
    /// is one a record was verified for writes on (<see cref="DeviceSelection"/>).
    /// </param>
    /// <exception cref="UnsupportedDeviceException">The laptop has no MSI WMI1 interface (WMI2 only, or none).</exception>
    /// <exception cref="PawnIoMissingException">The port is allowed and wanted, but PawnIO is not installed.</exception>
    /// <exception cref="EcAccessException">PawnIO or WMI1 does not answer.</exception>
    /// <exception cref="InvalidOperationException">An embedded device record did not load.</exception>
    public static EcSession Open(IAppLog log, EcBackends backend, Func<bool>? accessGate = null)
    {
        WmiInterfaceCheck.Default.EnsureWmi1(log);
        var catalog = DeviceConfigLoader.LoadEmbedded(log);
        var ecLock = new AccessEcMutex();
        PawnIoPortIo? ports = null;
        try
        {
            var firmware = ReadFirmware(catalog, ecLock, log, accessGate);
            var selection = DeviceSelection.Choose(firmware, catalog, EmbeddedDevices.Wmi1GenericId);
            var usePort = backend == EcBackends.Hybrid && selection.PortAllowed;
            if (usePort && PawnIoInstallation.InstalledVersion() is null)
            {
                throw new PawnIoMissingException();
            }

            var layout = EmbeddedDevices.LayoutOf(selection.Record);
            layout = usePort ? layout : layout.WithoutPortFeatures();
            var wmi = MsiWmiFields.Open(layout.Wmi);
            EcController? controller = null;
            EcTroubleLog? troubleLog = null;
            if (usePort)
            {
                ports = PawnIoPortIo.Open();
                troubleLog = new EcTroubleLog(log, TimeProvider.System, EcTroubleLog.DefaultSummaryInterval);
                controller = new EcController(ports, EcProtocolOptions.Default, troubleLog.Report);
            }

            var registers = RoutedEcRegisters.Create(wmi, controller, layout.Wmi);
            var worker = new EcWorker(registers, ecLock, LockTimeout, (message, ex) => log.Error(message, ex), accessGate);
            var fallback = backend == EcBackends.Hybrid && !usePort ? " (unverified firmware: port closed)" : "";
            log.Info($"EC session: {(usePort ? EcBackends.Hybrid : EcBackends.WmiOnly)}, device record {layout.Id}{fallback}");
            string[] warnings = firmware is null && backend == EcBackends.Hybrid ? [Strings.Session_FirmwareUnread] : [];
            return new EcSession(ports, ecLock, controller, worker, catalog, layout, log, troubleLog, warnings);
        }
        catch
        {
            ports?.Dispose();
            ecLock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The first stage: the firmware, read through WMI only with the P65's map. Every WMI1 record
    /// keeps the firmware at 0xA0 (all msi-ec groups) in <c>MSI_Software</c> ([assumption] for models
    /// not reported yet). Null when it cannot be read: the session then falls back without the port.
    /// </summary>
    /// <remarks>
    /// A failure is not an error: without a known firmware the port must stay closed, and failing the
    /// session would stop the start-up at log-on, when WMI may still be slow. The window says so
    /// (<see cref="StartupWarnings"/>) and a restart reads it again.
    /// </remarks>
    private static FirmwareInfo? ReadFirmware(IReadOnlyList<DeviceConfig> catalog, AccessEcMutex ecLock, IAppLog log, Func<bool>? accessGate)
    {
        var probe = EmbeddedDevices.Find(catalog, EmbeddedDevices.P65Id);
        var registers = RoutedEcRegisters.Create(MsiWmiFields.Open(probe.Wmi), null, probe.Wmi);
        using var worker = new EcWorker(registers, ecLock, LockTimeout, (message, ex) => log.Error(message, ex), accessGate);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return worker.RunRetryingAsync(ec => new P65Device(ec, probe).ReadFirmware(), EcAccessRetry.Default, log.Warn)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && attempt < FirmwareAttempts)
            {
                log.Warn($"Firmware unreadable (attempt {attempt}), retrying: {ex.Message}");
                Thread.Sleep(FirmwareRetryDelay);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                log.Warn($"Firmware unreadable; no device record chosen, opening with the port closed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Creates the gateway. On supported firmware the pre-M-Tool snapshot is captured first if it
    /// does not exist yet; without it (or on any EC/file trouble) the gateway stays locked.
    /// </summary>
    public async Task<EcGateway> CreateGatewayAsync(bool dryRun) =>
        (await WriteAccessBootstrap.CreateAsync(Worker, Layout, AppPaths.Root, dryRun, PortAvailable, _log).ConfigureAwait(false)).Gateway;

    /// <summary>Which record the firmware matched, logged once per call.</summary>
    public DeviceMatch Match(FirmwareInfo? firmware)
    {
        var match = FirmwareMatcher.Match(firmware, Catalog);
        _log.Info($"Device match: {match}, firmware {firmware?.Version ?? "unreadable"}");
        return match;
    }

    public void Dispose()
    {
        Worker.Dispose();
        _troubleLog?.Flush();
        _ecLock.Dispose();
        _ports?.Dispose();
    }
}
