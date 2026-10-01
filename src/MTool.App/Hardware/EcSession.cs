using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>How a session reaches the EC: MSI WMI with or without the raw port.</summary>
internal enum EcBackends
{
    /// <summary>WMI for everything it maps; the raw port (PawnIO) only for Cooler Boost and the charge limit.</summary>
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
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(500);

    private readonly PawnIoPortIo? _ports;
    private readonly AccessEcMutex _ecLock;
    private readonly IAppLog _log;
    private readonly EcTroubleLog? _troubleLog;

    private EcSession(
        PawnIoPortIo? ports, AccessEcMutex ecLock, EcController? controller, EcWorker worker, IAppLog log, EcTroubleLog? troubleLog)
    {
        _ports = ports;
        _ecLock = ecLock;
        Controller = controller;
        Worker = worker;
        _log = log;
        _troubleLog = troubleLog;
    }

    /// <summary>The raw port; null in <see cref="EcBackends.WmiOnly"/>.</summary>
    public EcController? Controller { get; }

    public EcWorker Worker { get; }

    /// <summary>Derived from the port the router was given, so the gateway's policy cannot disagree with it.</summary>
    public bool PortAvailable => Controller is not null;

    /// <param name="accessGate">When it returns false, EC operations fail without touching the EC (sleep/resume).</param>
    /// <exception cref="EcAccessException">PawnIO (Hybrid) or WMI1 is missing or does not answer.</exception>
    public static EcSession Open(IAppLog log, EcBackends backend, Func<bool>? accessGate = null)
    {
        if (backend == EcBackends.Hybrid && PawnIoInstallation.InstalledVersion() is null)
        {
            throw new EcAccessException($"PawnIO kurulu değil. Kurmak için: {PawnIoInstallation.InstallCommand}");
        }

        var wmi = MsiWmiFields.Open();
        var ecLock = new AccessEcMutex();
        PawnIoPortIo? ports = null;
        try
        {
            EcController? controller = null;
            EcTroubleLog? troubleLog = null;
            if (backend == EcBackends.Hybrid)
            {
                ports = PawnIoPortIo.Open();
                troubleLog = new EcTroubleLog(log, TimeProvider.System, EcTroubleLog.DefaultSummaryInterval);
                controller = new EcController(ports, EcProtocolOptions.Default, troubleLog.Report);
            }

            var registers = RoutedEcRegisters.Create(wmi, controller);
            var worker = new EcWorker(registers, ecLock, LockTimeout, (message, ex) => log.Error(message, ex), accessGate);
            log.Info($"EC oturumu: {backend}");
            return new EcSession(ports, ecLock, controller, worker, log, troubleLog);
        }
        catch
        {
            ports?.Dispose();
            ecLock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates the gateway. On supported firmware the pre-M-Tool snapshot is captured first if it
    /// does not exist yet; without it (or on any EC/file trouble) the gateway stays locked.
    /// </summary>
    public async Task<EcGateway> CreateGatewayAsync(bool dryRun) =>
        (await WriteAccessBootstrap.CreateAsync(Worker, AppPaths.Root, dryRun, PortAvailable, _log).ConfigureAwait(false)).Gateway;

    public void Dispose()
    {
        Worker.Dispose();
        _troubleLog?.Flush();
        _ecLock.Dispose();
        _ports?.Dispose();
    }
}
