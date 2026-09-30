using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;

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

    private EcSession(PawnIoPortIo ports, AccessEcMutex ecLock, EcController controller, EcWorker worker, IAppLog log)
    {
        _ports = ports;
        _ecLock = ecLock;
        Controller = controller;
        Worker = worker;
        _log = log;
    }

    public EcController Controller { get; }

    public EcWorker Worker { get; }

    public static EcSession Open(IAppLog log) => Open(log, EcProtocolOptions.Default);

    /// <param name="protocol">EC timing; only the stress test overrides the default.</param>
    /// <param name="observeTrouble">Also receives every EC retry/failure report (after it is logged).</param>
    public static EcSession Open(IAppLog log, EcProtocolOptions protocol, Action<EcTransactionTrouble>? observeTrouble = null)
    {
        if (PawnIoInstallation.InstalledVersion() is null)
        {
            throw new EcAccessException($"PawnIO kurulu değil. Kurmak için: {PawnIoInstallation.InstallCommand}");
        }

        var ports = PawnIoPortIo.Open();
        var ecLock = new AccessEcMutex();
        var controller = new EcController(ports, protocol, trouble =>
        {
            log.Warn($"EC: {trouble}");
            observeTrouble?.Invoke(trouble);
        });
        var worker = new EcWorker(controller, ecLock, LockTimeout, (message, ex) => log.Error(message, ex));
        return new EcSession(ports, ecLock, controller, worker, log);
    }

    public Task<T> ReadAsync<T>(Func<P65Device, T> read) => Worker.RunAsync(ec => read(new P65Device(ec)));

    /// <summary>
    /// Creates the gateway. On supported firmware the pre-M-Tool snapshot is captured first if it
    /// does not exist yet; without it the gateway stays locked.
    /// </summary>
    public async Task<EcGateway> CreateGatewayAsync(bool dryRun)
    {
        var firmware = await ReadAsync(device => device.ReadFirmware()).ConfigureAwait(false);
        var store = new PreStateStore(AppPaths.Root);
        if (firmware.IsSupported && !store.Exists)
        {
            // Only when no file exists: a corrupt snapshot is left for a human to look at.
            var state = await Worker.RunAsync(ec => PreStateCapture.Read(ec, firmware, DateTimeOffset.Now)).ConfigureAwait(false);
            store.SaveIfMissing(state);
            _log.Info($"M-Tool öncesi durum yedeklendi ({state.Registers.Count} register).");
        }

        var writeLock = new WriteLockStore(AppPaths.Root);
        var policy = new WritePolicy(
            FirmwareSupported: firmware.IsSupported,
            PreStateSaved: store.HasValidSnapshot(firmware.Version),
            DryRun: dryRun,
            PersistedLockReason: writeLock.Reason);
        return new EcGateway(Worker, policy, _log, writeLock.Lock);
    }

    public void Dispose()
    {
        Worker.Dispose();
        _ecLock.Dispose();
        _ports.Dispose();
    }
}
