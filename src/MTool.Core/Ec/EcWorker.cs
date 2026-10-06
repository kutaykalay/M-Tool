using System.Collections.Concurrent;

namespace MTool.Core.Ec;

/// <summary>
/// Owns all EC access on one dedicated thread. Operations queue up and run one at a time while
/// holding <see cref="IEcLock"/>, so the lock is always released on the thread that took it and
/// sensor polling can never interleave with a multi-register operation. Batch related reads into
/// one operation to keep them under a single lock hold.
/// </summary>
public sealed class EcWorker : IDisposable
{
    private readonly IEcRegisters _registers;
    private readonly IEcLock _ecLock;
    private readonly TimeSpan _lockTimeout;
    private readonly Action<string, Exception>? _reportError;
    private readonly Func<bool>? _accessGate;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private int _disposed;

    /// <param name="reportError">Receives errors that cannot reach a caller (e.g. a failed lock release).</param>
    /// <param name="accessGate">When it returns false, operations fail without touching the EC (sleep/resume).</param>
    public EcWorker(
        IEcRegisters registers,
        IEcLock ecLock,
        TimeSpan lockTimeout,
        Action<string, Exception>? reportError = null,
        Func<bool>? accessGate = null)
    {
        _registers = registers;
        _ecLock = ecLock;
        _lockTimeout = lockTimeout;
        _reportError = reportError;
        _accessGate = accessGate;
        _thread = new Thread(ProcessQueue) { IsBackground = true, Name = "M-Tool EC worker" };
        _thread.Start();
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    public Task<T> RunAsync<T>(Func<IEcRegisters, T> operation, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() => Execute(operation, completion, cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            throw new ObjectDisposedException(nameof(EcWorker));
        }

        return completion.Task;
    }

    /// <summary>
    /// Like <see cref="RunAsync{T}"/>, but every read rides out a silent EC period as the gateway
    /// does. For reads that matter (firmware, backup, control state), not for periodic sensor polls.
    /// </summary>
    public Task<T> RunRetryingAsync<T>(
        Func<IEcRegisters, T> operation, EcAccessRetry retry, Action<string> warn, CancellationToken cancellationToken = default) =>
        RunAsync(registers => operation(new RetryingEcReader(registers, retry, warn)), cancellationToken);

    /// <summary>Write-capable variant for <see cref="EcGateway"/> only.</summary>
    internal Task<T> RunWriteAsync<T>(Func<IEcWritableRegisters, T> operation, CancellationToken cancellationToken = default) =>
        RunAsync(
            registers => operation(registers as IEcWritableRegisters
                ?? throw new InvalidOperationException("The EC registers behind this worker are read-only.")),
            cancellationToken);

    /// <summary>
    /// Lets the running operation finish, fails everything still queued with
    /// <see cref="ObjectDisposedException"/> and stops the thread.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _queue.CompleteAdding();
        if (Thread.CurrentThread == _thread)
        {
            return; // Called from inside an operation; the loop ends on its own.
        }

        _thread.Join();
        _queue.Dispose();
    }

    private void ProcessQueue()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                _reportError?.Invoke("EC işleminde beklenmeyen hata", ex);
            }
        }
    }

    private void Execute<T>(
        Func<IEcRegisters, T> operation,
        TaskCompletionSource<T> completion,
        CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(EcWorker)));
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

        if (_accessGate is { } gate && !gate())
        {
            completion.TrySetException(new EcAccessException("EC erişimi uyku/uyanış nedeniyle duraklatıldı.") { IsAccessPaused = true });
            return;
        }

        var acquired = false;
        try
        {
            acquired = _ecLock.TryAcquire(_lockTimeout);
            if (!acquired)
            {
                completion.TrySetException(new EcAccessException(
                    $"EC kilidi (Access_EC) {_lockTimeout.TotalMilliseconds} ms içinde alınamadı; başka bir program EC'yi kullanıyor olabilir."));
                return;
            }

            completion.TrySetResult(operation(_registers));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            if (acquired)
            {
                Release();
            }
        }
    }

    private void Release()
    {
        try
        {
            _ecLock.Release();
        }
        catch (Exception ex)
        {
            _reportError?.Invoke("EC kilidi bırakılamadı", ex);
        }
    }
}
