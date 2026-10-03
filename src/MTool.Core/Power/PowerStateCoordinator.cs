namespace MTool.Core.Power;

/// <summary>
/// Stops EC access across sleep: from suspend until a settle delay after resume. Guards against
/// the post-sleep EC hang reported for YAMDCC (#145). Feed it Windows power events; pass
/// <see cref="IsEcAccessAllowed"/> to <see cref="Ec.EcWorker"/> as its access gate. The delay is
/// measured on the monotonic clock: a wall-clock step after wake (time sync) does not move it.
/// </summary>
public sealed class PowerStateCoordinator(TimeProvider time, TimeSpan resumeDelay)
{
    private readonly Lock _sync = new();
    private bool _suspended;
    private long? _resumedAt;

    public bool IsEcAccessAllowed
    {
        get
        {
            lock (_sync)
            {
                return !_suspended
                    && (_resumedAt is not { } resumedAt || time.GetElapsedTime(resumedAt) >= resumeDelay);
            }
        }
    }

    public void OnSuspend()
    {
        lock (_sync)
        {
            _suspended = true;
            _resumedAt = null;
        }
    }

    public void OnResume()
    {
        lock (_sync)
        {
            _suspended = false;
            _resumedAt = time.GetTimestamp();
        }
    }

    /// <summary>
    /// Feeds <paramref name="events"/> straight into the gate until disposed. For a reader with no
    /// reapply to schedule (<c>--watch</c>); the GUI's <see cref="AutoReapplier"/> drives the gate itself.
    /// </summary>
    public IDisposable Follow(IPowerEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        events.Suspending += OnSuspend;
        events.Resumed += OnResume;
        return new Subscription(events, this);
    }

    private sealed class Subscription(IPowerEvents events, PowerStateCoordinator coordinator) : IDisposable
    {
        public void Dispose()
        {
            events.Suspending -= coordinator.OnSuspend;
            events.Resumed -= coordinator.OnResume;
        }
    }
}
