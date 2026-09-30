namespace MTool.Core.Ec;

/// <summary>
/// Cross-process EC lock (the <c>Global\Access_EC</c> convention shared with LibreHardwareMonitor,
/// FanControl and others). Acquire and release must happen on the same thread.
/// </summary>
public interface IEcLock
{
    bool TryAcquire(TimeSpan timeout);

    void Release();
}
