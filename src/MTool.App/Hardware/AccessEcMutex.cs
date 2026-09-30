using System.Security.AccessControl;
using System.Security.Principal;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>
/// The <c>Global\Access_EC</c> mutex other EC tools (LHM, FanControl, HWiNFO) also take. Created
/// with an Everyone ACL, as LibreHardwareMonitor does, so tools running as other users can open it.
/// </summary>
internal sealed class AccessEcMutex : IEcLock, IDisposable
{
    private const string Name = @"Global\Access_EC";
    private readonly Mutex _mutex = CreateOrOpen();

    public bool TryAcquire(TimeSpan timeout)
    {
        try
        {
            return _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died holding it; we now own it.
            return true;
        }
    }

    public void Release() => _mutex.ReleaseMutex();

    public void Dispose() => _mutex.Dispose();

    private static Mutex CreateOrOpen()
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), MutexRights.FullControl, AccessControlType.Allow));
        try
        {
            return MutexAcl.Create(initiallyOwned: false, Name, out _, security);
        }
        catch (UnauthorizedAccessException)
        {
            // Another tool created it with a stricter ACL; opening it is enough to take part.
            return Mutex.OpenExisting(Name);
        }
    }
}
