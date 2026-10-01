using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>
/// Opens PawnIO, the Access_EC lock and the EC worker, reads the firmware and makes sure the
/// "before M-Tool" snapshot exists before any gateway can be created.
/// </summary>
internal sealed class EcSession : IDisposable
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(500);

    private readonly PawnIoPortIo _ports;
    private readonly AccessEcMutex _ecLock;
    private readonly IAppLog _log;
    private readonly EcTroubleLog _troubleLog;

    private EcSession(
        PawnIoPortIo ports, AccessEcMutex ecLock, EcController controller, EcWorker worker, IAppLog log, EcTroubleLog troubleLog)
    {
        _ports = ports;
        _ecLock = ecLock;
        Controller = controller;
        Worker = worker;
        _log = log;
        _troubleLog = troubleLog;
    }

    public EcController Controller { get; }

    public EcWorker Worker { get; }

    public static EcSession Open(IAppLog log) => Open(log, EcProtocolOptions.Default);

    /// <param name="protocol">EC timing; only the stress test overrides the default.</param>
    /// <param name="observeTrouble">Also receives every EC retry/failure report (after the log has seen it).</param>
    public static EcSession Open(IAppLog log, EcProtocolOptions protocol, Action<EcTransactionTrouble>? observeTrouble = null)
    {
        if (PawnIoInstallation.InstalledVersion() is null)
        {
            throw new EcAccessException($"PawnIO kurulu değil. Kurmak için: {PawnIoInstallation.InstallCommand}");
        }

        var ports = PawnIoPortIo.Open();
        var ecLock = new AccessEcMutex();
        var troubleLog = new EcTroubleLog(log, TimeProvider.System, EcTroubleLog.DefaultSummaryInterval);
        var controller = new EcController(ports, protocol, trouble =>
        {
            troubleLog.Report(trouble);
            observeTrouble?.Invoke(trouble);
        });
        var worker = new EcWorker(controller, ecLock, LockTimeout, (message, ex) => log.Error(message, ex));
        return new EcSession(ports, ecLock, controller, worker, log, troubleLog);
    }

    public Task<T> ReadAsync<T>(Func<P65Device, T> read) => Worker.RunAsync(ec => read(new P65Device(ec)));

    /// <summary>
    /// Creates the gateway. On supported firmware the pre-M-Tool snapshot is captured first if it
    /// does not exist yet; without it (or on any EC/file trouble) the gateway stays locked.
    /// </summary>
    public async Task<EcGateway> CreateGatewayAsync(bool dryRun) =>
        (await WriteAccessBootstrap.CreateAsync(Worker, AppPaths.Root, dryRun, portAvailable: true, _log).ConfigureAwait(false)).Gateway;

    public void Dispose()
    {
        Worker.Dispose();
        _troubleLog.Flush();
        _ecLock.Dispose();
        _ports.Dispose();
    }
}
