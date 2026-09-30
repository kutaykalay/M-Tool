using System.Collections.Concurrent;
using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

internal sealed class FakeEcLock : IEcLock
{
    private volatile bool _held;

    public volatile bool Available = true;
    public volatile Exception? AcquireError;
    public volatile Exception? ReleaseError;

    public bool Held => _held;
    public ConcurrentQueue<(string Action, int ThreadId)> Events { get; } = new();

    public bool TryAcquire(TimeSpan timeout)
    {
        if (AcquireError is { } error)
        {
            throw error;
        }

        if (!Available)
        {
            return false;
        }

        _held = true;
        Events.Enqueue(("acquire", Environment.CurrentManagedThreadId));
        return true;
    }

    public void Release()
    {
        _held = false;
        Events.Enqueue(("release", Environment.CurrentManagedThreadId));
        if (ReleaseError is { } error)
        {
            throw error;
        }
    }
}
