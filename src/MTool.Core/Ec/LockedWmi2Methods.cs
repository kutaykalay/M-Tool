namespace MTool.Core.Ec;

/// <summary>
/// Takes the shared EC lock around each WMI2 call. The report opens its own <c>MSI_ACPI</c>
/// session; the lock keeps its calls from running alongside a live round (which holds the lock for
/// the whole round) or another EC tool.
/// </summary>
public sealed class LockedWmi2Methods(IWmi2Methods inner, IEcLock ecLock, TimeSpan lockTimeout) : IWmi2Methods
{
    /// <exception cref="EcAccessException">The lock was not free in time (no call made), or the call failed.</exception>
    public byte[] Call(Wmi2Request request)
    {
        if (!ecLock.TryAcquire(lockTimeout))
        {
            throw new EcAccessException(
                $"{request.Method}(0x{request.Sub:X2}): the EC lock was not free within {lockTimeout.TotalMilliseconds} ms; no call made.");
        }

        try
        {
            return inner.Call(request);
        }
        finally
        {
            ecLock.Release();
        }
    }
}
