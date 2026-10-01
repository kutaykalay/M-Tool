using System.Threading;

namespace MTool.App.Startup;

/// <summary>
/// One GUI per user session. A second start exits; a plain one first asks the running GUI to show
/// its window, a <c>--tray</c> one (the sign-in task meeting a GUI already open) does not.
/// The CLI does not take this lock: it is the recovery path and must work while the GUI runs.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\M-Tool.Gui";
    private const string ShowEventName = @"Local\M-Tool.Gui.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequested;
    private RegisteredWaitHandle? _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle showRequested) => (_mutex, _showRequested) = (mutex, showRequested);

    /// <summary>The lock, or null when a GUI already runs (signalled to show itself if <paramref name="askToShow"/>).</summary>
    public static SingleInstance? TryAcquire(bool askToShow)
    {
        // The event exists before the mutex, so a second start can always signal the first.
        var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex, showRequested);
        }

        mutex.Dispose();
        using (showRequested)
        {
            if (askToShow)
            {
                showRequested.Set();
            }
        }

        return null;
    }

    /// <summary><paramref name="onShowRequested"/> runs on a thread-pool thread.</summary>
    public void ListenForShowRequests(Action onShowRequested) =>
        _listener = ThreadPool.RegisterWaitForSingleObject(
            _showRequested, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _listener?.Unregister(null);
        _showRequested.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
