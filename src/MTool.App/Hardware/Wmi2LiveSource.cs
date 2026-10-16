using MTool.App.ViewModels;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>
/// The live WMI2 values for the reports-only window: one <see cref="MsiWmi2Session"/> behind an
/// <see cref="EcWorker"/> over <see cref="Wmi2EcRegisters"/>, so every read holds Access_EC like a
/// WMI1 read, runs on one thread, and stops while the access gate is closed. Read only by type: the
/// registers are not writable and the session calls only the pinned Get_ methods.
/// </summary>
internal sealed class Wmi2LiveSource : IWmi2LiveSource
{
    private const string FirmwareMethod = "Get_EC";
    private const byte FirmwareSub = 0;

    private readonly IWmi2Methods _wmi;
    private readonly IDisposable _owned;
    private readonly EcWorker _worker;

    /// <param name="owned">Closed after the worker has stopped: the session, and the lock when this source made it.</param>
    internal Wmi2LiveSource(IWmi2Methods wmi, IDisposable owned, IEcLock ecLock, Func<bool> accessGate, IAppLog log)
    {
        (_wmi, _owned) = (wmi, owned);
        _worker = new EcWorker(new Wmi2EcRegisters(wmi), ecLock, EcSession.LockTimeout, (message, ex) => log.Error(message, ex), accessGate);
    }

    /// <summary>Probes the interface again and opens it only on a WMI2 laptop; null otherwise (nothing opened).</summary>
    /// <exception cref="EcAccessException">The interface cannot be opened.</exception>
    public static Wmi2LiveSource? Open(Func<bool> accessGate, IAppLog log)
    {
        if (Wmi2Live.OpenIfWmi2(WmiProbe.Detect, MsiWmi2Session.Open, log) is not { } session)
        {
            return null;
        }

        AccessEcMutex? ecLock = null;
        try
        {
            ecLock = new AccessEcMutex();
            return new Wmi2LiveSource(session, new Both(session, ecLock), ecLock, accessGate, log);
        }
        catch
        {
            ecLock?.Dispose();
            session.Dispose();
            throw;
        }
    }

    /// <summary>One Get_EC call; the version as the report reads it (Wmi2Readout).</summary>
    public Task<string?> ReadFirmwareAsync(CancellationToken cancellationToken) =>
        _worker.RunAsync(_ =>
        {
            var packet = _wmi.Call(Wmi2Calls.Request(FirmwareMethod, FirmwareSub));
            return new Wmi2Readout([new Wmi2Call(FirmwareMethod, FirmwareSub, packet, null)]).Interpret().Firmware;
        }, cancellationToken);

    public Task<Wmi2Status> ReadStatusAsync(CancellationToken cancellationToken) =>
        _worker.RunAsync(Wmi2Family.ReadStatus, cancellationToken);

    public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken) =>
        _worker.RunAsync(Wmi2Family.ReadSensors, cancellationToken);

    /// <summary>The worker first (it lets a running read finish), then the session and the lock.</summary>
    public void Dispose()
    {
        try
        {
            _worker.Dispose();
        }
        finally
        {
            _owned.Dispose();
        }
    }

    private sealed class Both(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                first.Dispose();
            }
            finally
            {
                second.Dispose();
            }
        }
    }
}
