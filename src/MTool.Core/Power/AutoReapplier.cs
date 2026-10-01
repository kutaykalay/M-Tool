using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Power;

/// <summary>
/// Decides when the desired state is written back after the EC reset it: once at start-up, and
/// <see cref="AutoReapplyOptions.ResumeDelay"/> after every wake. Sleep closes the EC access gate
/// and cancels whatever is waiting; several wakes in a row give one reapply. A reapply that ends
/// <see cref="WriteStatus.Rejected"/> (nothing written, e.g. the EC was not ready), or that finds the
/// gate still closed, is retried once after <see cref="AutoReapplyOptions.RetryDelay"/>; a failed
/// write never is. Every reapply runs with <see cref="PortUse.None"/>, so this path never touches
/// the raw port. Nothing thrown escapes.
/// </summary>
/// <param name="reapply">Writes the desired state; see <see cref="ProfileService.ReapplyAsync"/>.</param>
/// <param name="coordinator">The EC access gate this class opens and closes; built with <see cref="AutoReapplyOptions.GateDelay"/>.</param>
public sealed class AutoReapplier(
    Func<PortUse, Task<IReadOnlyList<WriteOutcome>>> reapply,
    IPowerEvents events,
    PowerStateCoordinator coordinator,
    TimeProvider time,
    IAppLog log,
    AutoReapplyOptions options) : IDisposable
{
    private readonly AutoReapplyOptions _options = Validated(options);
    private readonly Lock _sync = new();
    private ITimer? _waiting;
    private long _generation;
    private int _running;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Raised after each reapply that returned, on the thread that finished it. A reapply overtaken by
    /// sleep or wake is still reported (it did write), so its result may arrive just before a newer one.
    /// </summary>
    public event Action<AutoReapplyResult>? Reapplied;

    /// <summary>
    /// True while a reapply that started before <see cref="Dispose"/> is still running. After
    /// Dispose no new one starts, so shutdown can wait for this before closing the EC session.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _running > 0;
            }
        }
    }

    /// <summary>Subscribes to power events and reapplies once now. Later calls do nothing.</summary>
    public void Start()
    {
        long generation;
        lock (_sync)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            generation = _generation;

            // Under the lock, so a racing Dispose cannot unsubscribe before this subscribes.
            events.Suspending += OnSuspending;
            events.Resumed += OnResumed;
        }

        _ = RunAsync(ReapplyTrigger.Startup, generation);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelWaitingLocked();
            events.Suspending -= OnSuspending;
            events.Resumed -= OnResumed;
        }
    }

    private void OnSuspending() => Guarded("uyku", () =>
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            coordinator.OnSuspend();
            CancelWaitingLocked();
        }
    });

    private void OnResumed() => Guarded("uyanış", () =>
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            coordinator.OnResume();
            CancelWaitingLocked();
            ScheduleLocked(ReapplyTrigger.Resume, _options.ResumeDelay);
        }
    });

    /// <summary>A new generation makes every waiting or running job stale: it neither runs nor retries.</summary>
    private void CancelWaitingLocked()
    {
        _generation++;
        _waiting?.Dispose();
        _waiting = null;
    }

    private void ScheduleLocked(ReapplyTrigger trigger, TimeSpan delay)
    {
        var generation = _generation;
        _waiting?.Dispose();
        _waiting = time.CreateTimer(_ => OnTimer(trigger, generation), null, delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Fire and forget: <see cref="RunAsync"/> catches everything itself.</summary>
    private void OnTimer(ReapplyTrigger trigger, long generation) => _ = RunAsync(trigger, generation);

    /// <summary>Counts the run in under the same lock Dispose takes, so Dispose never misses one.</summary>
    private bool TryEnter(long generation)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation)
            {
                return false;
            }

            _running++;
            return true;
        }
    }

    private void Exit()
    {
        lock (_sync)
        {
            _running--;
        }
    }

    private async Task RunAsync(ReapplyTrigger trigger, long generation)
    {
        if (!TryEnter(generation))
        {
            return;
        }

        try
        {
            // The resume delay outlasts the gate, but if the gate is still shut (a late wake event, a slow
            // timer clock) the reapply is retried, not dropped.
            if (!coordinator.IsEcAccessAllowed)
            {
                log.Warn($"Otomatik yeniden uygulama ({trigger}) ertelendi: EC erişimi henüz kapalı");
                RetryIfStillCurrent(trigger, generation);
                return;
            }

            var outcomes = await reapply(PortUse.None).ConfigureAwait(false);
            if (ReapplySummary.Worst(outcomes)?.Status == WriteStatus.Rejected)
            {
                RetryIfStillCurrent(trigger, generation);
            }

            Report(new AutoReapplyResult(trigger, outcomes));
        }
        catch (Exception ex)
        {
            Guarded("hata kaydı", () => log.Error($"Otomatik yeniden uygulama ({trigger}) hata verdi", ex));
        }
        finally
        {
            Exit();
        }
    }

    private void RetryIfStillCurrent(ReapplyTrigger trigger, long generation)
    {
        lock (_sync)
        {
            if (trigger == ReapplyTrigger.Retry || _disposed || generation != _generation)
            {
                return;
            }

            log.Info($"Otomatik yeniden uygulama {_options.RetryDelay.TotalSeconds:0} sn sonra bir kez yeniden denenecek");
            ScheduleLocked(ReapplyTrigger.Retry, _options.RetryDelay);
        }
    }

    private void Report(AutoReapplyResult result)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            Reapplied?.Invoke(result);
        }
        catch (Exception ex)
        {
            log.Error("Otomatik yeniden uygulama sonucu bildirilemedi", ex);
        }
    }

    /// <summary>Power events arrive on system threads: nothing may escape to them.</summary>
    private void Guarded(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                log.Error($"Otomatik yeniden uygulama: {what} işlenemedi", ex);
            }
            catch
            {
                // The log itself failed; there is nowhere left to report.
            }
        }
    }

    private static AutoReapplyOptions Validated(AutoReapplyOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.GateDelay, TimeSpan.Zero, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ResumeDelay, TimeSpan.Zero, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.RetryDelay, TimeSpan.Zero, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ResumeDelay, options.GateDelay, nameof(options));
        return options;
    }
}
