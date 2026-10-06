using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Power;

/// <summary>
/// Tells <see cref="ProfileService.SwitchPowerSourceAsync"/> when the laptop moved between AC and
/// battery: <see cref="PowerSwitchOptions.Debounce"/> after the last change, so a loose plug gives one
/// switch. Sleep cancels a waiting change. On wake the source is read at once without writing: the
/// reapply after wake (<see cref="AutoReapplier"/>) then writes the new source's pair. A change found
/// while the EC access gate is shut is not written either, for the same reason. Nothing thrown escapes.
/// </summary>
/// <param name="switch">The service's switch; returns the write outcomes, or null when it wrote nothing.</param>
public sealed class PowerSourceSwitcher(
    Func<PowerSource, bool, Task<IReadOnlyList<WriteOutcome>?>> @switch,
    IPowerSource source,
    IPowerEvents events,
    PowerStateCoordinator coordinator,
    TimeProvider time,
    IAppLog log,
    PowerSwitchOptions options) : IDisposable
{
    private readonly PowerSwitchOptions _options = Validated(options);
    private readonly Lock _sync = new();
    private ITimer? _waiting;
    private long _generation;
    private int _running;
    private bool _started;
    private bool _disposed;

    /// <summary>Raised after a switch that wrote, with <see cref="ReapplyTrigger.PowerSource"/>, on the thread that finished it.</summary>
    public event Action<AutoReapplyResult>? Switched;

    /// <summary>True while a switch that started before <see cref="Dispose"/> has not returned.</summary>
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

    /// <summary>Subscribes and acts on the source once now (it may have changed since start-up). Later calls do nothing.</summary>
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
            source.Changed += OnChanged;
            events.Suspending += OnSuspending;
            events.Resumed += OnResumed;
        }

        _ = RunAsync(generation, mayWrite: true);
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
            source.Changed -= OnChanged;
            events.Suspending -= OnSuspending;
            events.Resumed -= OnResumed;
        }
    }

    private void OnChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            CancelWaitingLocked();
            var generation = _generation;
            _waiting = time.CreateTimer(_ => _ = RunAsync(generation, mayWrite: true), null, _options.Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSuspending()
    {
        lock (_sync)
        {
            CancelWaitingLocked();
        }
    }

    private void OnResumed()
    {
        long generation;
        lock (_sync)
        {
            CancelWaitingLocked();
            generation = _generation;
        }

        _ = RunAsync(generation, mayWrite: false);
    }

    /// <summary>A new generation makes a waiting change stale: it does not run.</summary>
    private void CancelWaitingLocked()
    {
        _generation++;
        _waiting?.Dispose();
        _waiting = null;
    }

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

    private async Task RunAsync(long generation, bool mayWrite)
    {
        if (!TryEnter(generation))
        {
            return;
        }

        try
        {
            if (source.Current is not { } current)
            {
                return;
            }

            var outcomes = await @switch(current, mayWrite && coordinator.IsEcAccessAllowed).ConfigureAwait(false);
            if (outcomes is not null)
            {
                Switched?.Invoke(new AutoReapplyResult(ReapplyTrigger.PowerSource, outcomes));
            }
        }
        catch (Exception ex)
        {
            try
            {
                log.Error("Güç kaynağı geçişi hata verdi", ex);
            }
            catch
            {
                // The log itself failed; there is nowhere left to report.
            }
        }
        finally
        {
            lock (_sync)
            {
                _running--;
            }
        }
    }

    private static PowerSwitchOptions Validated(PowerSwitchOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Debounce, TimeSpan.Zero, nameof(options));
        return options;
    }
}
