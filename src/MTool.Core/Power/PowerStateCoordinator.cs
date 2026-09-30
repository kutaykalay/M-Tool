namespace MTool.Core.Power;

/// <summary>
/// Stops EC access across sleep: from suspend until a settle delay after resume. Guards against
/// the post-sleep EC hang reported for YAMDCC (#145). Feed it Windows power events; pass
/// <see cref="IsEcAccessAllowed"/> to <see cref="Ec.EcWorker"/> as its access gate.
/// </summary>
public sealed class PowerStateCoordinator(TimeProvider time, TimeSpan resumeDelay)
{
    private readonly Lock _sync = new();
    private bool _suspended;
    private DateTimeOffset? _resumedAt;

    public bool IsEcAccessAllowed
    {
        get
        {
            lock (_sync)
            {
                return !_suspended
                    && (_resumedAt is not { } resumedAt || time.GetUtcNow() - resumedAt >= resumeDelay);
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
            _resumedAt = time.GetUtcNow();
        }
    }
}
