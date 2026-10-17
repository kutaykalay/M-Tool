using MTool.Core.Diagnostics;
using MTool.Core.Ec;
using MTool.Core.Localization;

namespace MTool.Core.Sensors;

/// <summary>WMI2 reads were stopped after failures in a row; no WMI2 call was made. The message is for the log, not the window.</summary>
public sealed class Wmi2StoppedException(string message) : Exception(message);

/// <summary>
/// Stops WMI2 reads after <see cref="MaxFailuresInARow"/> failed rounds in a row; after that no read
/// runs until <see cref="Reset"/>, which only a user action calls. A round counts when it ends, so a
/// hung one counts once it finally fails: the caller bounds each call (<c>MsiWmiFields.Bounded</c>).
/// Sleep (the access gate) and cancellation are not failures, and a round that started before the
/// last <see cref="Reset"/> does not count at all.
/// </summary>
public sealed class Wmi2CircuitBreaker
{
    public const int MaxFailuresInARow = Wmi2Probe.MaxFailuresInARow;

    private readonly Lock _sync = new();
    private int _failuresInARow;
    private int _generation;
    private string? _lastError;

    public bool IsOpen
    {
        get
        {
            lock (_sync)
            {
                return _failuresInARow >= MaxFailuresInARow;
            }
        }
    }

    /// <summary>The last failure's message since the last <see cref="Reset"/>, for the log; null before any.</summary>
    public string? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    /// <exception cref="Wmi2StoppedException">The breaker is open; <paramref name="read"/> was not called.</exception>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        int generation;
        lock (_sync)
        {
            if (_failuresInARow >= MaxFailuresInARow)
            {
                throw new Wmi2StoppedException($"WMI2 reads stopped after {MaxFailuresInARow} failed reads in a row: {_lastError}");
            }

            generation = _generation;
        }

        try
        {
            var value = await read(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                // A late success must not close an open breaker: only Reset does.
                if (generation == _generation && _failuresInARow < MaxFailuresInARow)
                {
                    _failuresInARow = 0;
                }
            }

            return value;
        }
        catch (Exception ex) when (Counts(ex, cancellationToken))
        {
            lock (_sync)
            {
                if (generation == _generation)
                {
                    _failuresInARow++;
                    _lastError = ex.ForLog();
                }
            }

            throw;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _failuresInARow = 0;
            _lastError = null;
            _generation++;
        }
    }

    private static bool Counts(Exception ex, CancellationToken cancellationToken) =>
        !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        && ex is not EcAccessException { IsAccessPaused: true };
}
