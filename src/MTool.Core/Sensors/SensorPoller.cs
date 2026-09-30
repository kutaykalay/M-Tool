using MTool.Core.Device;

namespace MTool.Core.Sensors;

/// <summary>
/// Reads the sensors on a timer (1 s with the window open, 5 s for the tray tooltip). The EC goes
/// silent for 117-252 ms now and then (plan.md §8), so a failed poll is skipped and the last good
/// values stay; only <see cref="StaleAfterMisses"/> misses in a row mark the data stale. Nothing
/// thrown by a read escapes, polls never overlap, and the log gets a line only when the state changes.
/// </summary>
/// <param name="read">One sensor read; see <see cref="IP65Control.ReadSensorsAsync"/>.</param>
/// <param name="accessGate">False while EC access is paused (sleep/resume): the EC is not touched.</param>
/// <param name="isWriteInProgress">A miss while a write holds the EC is expected and not counted.</param>
public sealed class SensorPoller(
    Func<CancellationToken, Task<SensorSnapshot>> read,
    Func<bool> accessGate,
    Func<bool> isWriteInProgress,
    TimeProvider time,
    IAppLog log,
    TimeSpan interval) : IDisposable
{
    public const int StaleAfterMisses = 3;

    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    private readonly PeriodicTimer _timer = new(interval, time);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _polling = new(1, 1);
    private SensorReading _latest = SensorReading.Initial;
    private Task<SensorSnapshot>? _pendingRead;
    private int _started;

    /// <summary>Raised on a thread-pool thread whenever <see cref="Latest"/> changes.</summary>
    public event Action<SensorReading>? ReadingChanged;

    public SensorReading Latest => Volatile.Read(ref _latest);

    public TimeSpan Interval => _timer.Period;

    /// <summary>Polls once now, then on every tick until disposed.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = RunAsync(_stop.Token);
        }
    }

    /// <summary>Ignored after dispose (the window may still be closing).</summary>
    public void SetInterval(TimeSpan value)
    {
        if (!_stop.IsCancellationRequested)
        {
            _timer.Period = value;
        }
    }

    /// <summary>
    /// One poll; skipped when another poll is running or after dispose. A read still stuck from an
    /// earlier poll is not repeated but counts as a miss, so a hung EC turns the data stale.
    /// </summary>
    public async Task PollNowAsync()
    {
        if (_stop.IsCancellationRequested || !await _polling.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (_pendingRead is { IsCompleted: false })
            {
                RecordMiss(Latest, new TimeoutException("önceki sensör okuması hâlâ bitmedi"));
                return;
            }

            await PollLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _polling.Release();
        }
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _stop.Cancel();
        _timer.Dispose();
    }

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            do
            {
                await PollNowAsync().ConfigureAwait(false);
            }
            while (await _timer.WaitForNextTickAsync(stop).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (Exception ex)
        {
            log.Error("Sensör yoklama döngüsü durdu", ex);
        }
    }

    private async Task PollLockedAsync()
    {
        var previous = Latest;
        if (!accessGate())
        {
            Publish(previous with { Status = SensorStatus.Paused, ConsecutiveMisses = 0 });
            return;
        }

        SensorReading reading;
        try
        {
            _pendingRead = read(_stop.Token);
            var snapshot = await _pendingRead.WaitAsync(ReadTimeout, time, _stop.Token).ConfigureAwait(false);
            if (previous.ConsecutiveMisses > 0)
            {
                log.Info($"Sensör okuma düzeldi ({previous.ConsecutiveMisses} örnek kaçtı).");
            }

            reading = new SensorReading(SensorReading.Merge(previous.Snapshot, snapshot), SensorStatus.Live, 0, time.GetUtcNow());
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Disposed mid-read: publish nothing.
            return;
        }
        catch (Exception ex)
        {
            RecordMiss(previous, ex);
            return;
        }

        Publish(reading);
    }

    private void RecordMiss(SensorReading previous, Exception error)
    {
        if (isWriteInProgress())
        {
            return;
        }

        var misses = previous.ConsecutiveMisses + 1;
        // Values from before a pause are old by definition.
        var status = misses >= StaleAfterMisses || previous.Status is SensorStatus.Paused ? SensorStatus.Stale
            : previous.Status;
        if (misses == 1)
        {
            log.Warn($"Sensör okuma başarısız, örnek atlandı: {error.Message}");
        }
        else if (misses == StaleAfterMisses)
        {
            log.Warn($"Sensör verisi eski: {misses} okuma üst üste başarısız ({error.Message}).");
        }

        Publish(previous with { Status = status, ConsecutiveMisses = misses });
    }

    private void Publish(SensorReading reading)
    {
        if (_stop.IsCancellationRequested || reading == Latest)
        {
            return;
        }

        Volatile.Write(ref _latest, reading);
        try
        {
            ReadingChanged?.Invoke(reading);
        }
        catch (Exception ex)
        {
            // A subscriber's bug must not stop polling or be mistaken for an EC miss.
            log.Error("Sensör verisi abonesi hata verdi", ex);
        }
    }
}
