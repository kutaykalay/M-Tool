using System.Diagnostics;

namespace MTool.Core.Ec;

/// <summary>
/// ACPI EC read/write protocol (ACPI spec ch. 12) over raw ports. Windows' own EC driver does not
/// take our lock and can interleave with us, so every step waits on the status flags and an answer
/// is rejected when another byte is already queued behind it. A byte already waiting before we
/// start belongs to someone else (e.g. Windows' answer to an EC event query): we let its owner
/// take it and only drain it when nobody has for a whole step. Only after an unanswered read do
/// we drain right away, because a late byte then is our own answer.
/// Writes are only reachable through the internal <see cref="IEcWritableRegisters"/>,
/// i.e. through <see cref="EcGateway"/>. Not thread-safe: use it only from <see cref="EcWorker"/>.
/// </summary>
public sealed class EcController : IEcRegisters, IEcWritableRegisters
{
    private const byte DataPort = 0x62;
    private const byte CommandPort = 0x66;
    private const byte ReadCommand = 0x80;
    private const byte WriteCommand = 0x81;
    private const byte OutputBufferFull = 0x01;
    private const byte InputBufferFull = 0x02;
    private const int SpinPollsBeforeYield = 20;
    private const int RegisterCount = 256;

    private readonly IPortIo _ports;
    private readonly EcProtocolOptions _options;
    private readonly Action<EcTransactionTrouble>? _onTrouble;
    private int _recoveredFailures;
    private byte _lastStatus;
    private List<byte> _drained = [];

    /// <param name="onTrouble">
    /// Called after a read or write that needed a retry or failed, once the transaction is over so
    /// that reporting never stretches the gap between attempts. Exceptions from it are ignored.
    /// </param>
    public EcController(IPortIo ports, EcProtocolOptions? options = null, Action<EcTransactionTrouble>? onTrouble = null)
    {
        _ports = ports;
        _options = options ?? EcProtocolOptions.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxAttempts, 1, nameof(options));
        _onTrouble = onTrouble;
    }

    /// <summary>Failed attempts that a later retry recovered from (diagnostics, safe to read from any thread).</summary>
    public int RecoveredFailures => Volatile.Read(ref _recoveredFailures);

    public byte Read(byte register)
    {
        byte value = 0;
        Run(EcOperation.Read, register, () => TryRead(register, out value));
        return value;
    }

    /// <summary>Protocol-level write with retries. Does not verify; the gateway reads back.</summary>
    void IEcWritableRegisters.Write(byte register, byte value) =>
        Run(EcOperation.Write, register, () => TryWrite(register, value));

    public IReadOnlyList<byte> ReadBlock(byte startRegister, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startRegister + count, RegisterCount, nameof(count));

        var values = new byte[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = Read((byte)(startRegister + i));
        }

        return Array.AsReadOnly(values);
    }

    private void Run(EcOperation operation, byte register, Func<EcFailureKind?> attempt)
    {
        List<EcFailedAttempt>? failures = null;
        for (var number = 1; number <= _options.MaxAttempts; number++)
        {
            _drained = [];
            if (attempt() is not { } kind)
            {
                Interlocked.Add(ref _recoveredFailures, number - 1);
                Report(operation, register, succeeded: true, failures);
                return;
            }

            var status = _lastStatus;
            if (kind == EcFailureKind.NoAnswer)
            {
                Settle();
            }

            (failures ??= []).Add(new EcFailedAttempt(number, kind, status, _drained.AsReadOnly()));
        }

        Report(operation, register, succeeded: false, failures);
        var verb = operation == EcOperation.Read ? "read" : "written";
        throw new EcAccessException(
            $"EC register 0x{register:X2} could not be {verb} after {_options.MaxAttempts} attempts " +
            $"({string.Join(", ", failures!.Select(f => f.Kind))}).");
    }

    private void Report(EcOperation operation, byte register, bool succeeded, List<EcFailedAttempt>? failures)
    {
        if (failures is null || _onTrouble is null)
        {
            return;
        }

        try
        {
            _onTrouble(new EcTransactionTrouble(operation, register, succeeded, failures.AsReadOnly()));
        }
        catch (Exception)
        {
            // Diagnostics must never turn a completed EC access into a failure.
        }
    }

    private EcFailureKind? TryRead(byte register, out byte value)
    {
        value = 0;
        if (ClaimOutputBuffer() is { } busy)
        {
            return busy;
        }

        if (!WaitUntil(InputBufferFull, set: false))
        {
            return EcFailureKind.InputBufferBusy;
        }

        _ports.Out(CommandPort, ReadCommand);
        if (!WaitUntil(InputBufferFull, set: false))
        {
            return EcFailureKind.InputBufferBusy;
        }

        _ports.Out(DataPort, register);
        if (!WaitUntil(OutputBufferFull, set: true))
        {
            return EcFailureKind.NoAnswer;
        }

        value = _ports.In(DataPort);

        // Another byte already waiting means the stream is out of step: we cannot tell whether
        // the byte we took was ours or a late/foreign one.
        return OutputPending() ? EcFailureKind.ExtraByteAfterAnswer : null;
    }

    private EcFailureKind? TryWrite(byte register, byte value)
    {
        if (ClaimOutputBuffer() is { } busy)
        {
            return busy;
        }

        foreach (var (port, datum) in new[] { (CommandPort, WriteCommand), (DataPort, register), (DataPort, value) })
        {
            if (!WaitUntil(InputBufferFull, set: false))
            {
                return EcFailureKind.InputBufferBusy;
            }

            _ports.Out(port, datum);
        }

        return WaitUntil(InputBufferFull, set: false) ? null : EcFailureKind.InputBufferBusy;
    }

    /// <summary>
    /// Makes sure the output buffer is empty before a transaction. A waiting byte is someone
    /// else's: Windows collects its own promptly, so we wait for that. Still there after a whole
    /// step, it is an orphan (e.g. a late answer nobody waits for) and is drained.
    /// </summary>
    private EcFailureKind? ClaimOutputBuffer()
    {
        if (!OutputPending() || WaitUntil(OutputBufferFull, set: false))
        {
            return null;
        }

        _drained.Add(_ports.In(DataPort));
        return EcFailureKind.OutputPendingAtStart;
    }

    private byte ReadStatus() => _lastStatus = _ports.In(CommandPort);

    private bool OutputPending() => (ReadStatus() & OutputBufferFull) != 0;

    /// <summary>
    /// After an unanswered read: drains the output buffer until it has stayed empty for the
    /// configured number of polls, so our late answer is not taken as the next one.
    /// </summary>
    private void Settle()
    {
        var deadline = Stopwatch.StartNew();
        var clearPolls = 0;
        for (var poll = 0;
             poll < _options.EffectiveMaxStatusPolls && clearPolls < _options.SettleClearPolls
             && deadline.Elapsed <= _options.EffectiveStepTimeout;
             poll++)
        {
            if (OutputPending())
            {
                _drained.Add(_ports.In(DataPort));
                clearPolls = 0;
            }
            else
            {
                clearPolls++;
            }
        }
    }

    private bool WaitUntil(byte flag, bool set)
    {
        var deadline = Stopwatch.StartNew();
        for (var poll = 0; poll < _options.EffectiveMaxStatusPolls; poll++)
        {
            var isSet = (ReadStatus() & flag) != 0;
            if (isSet == set)
            {
                return true;
            }

            if (deadline.Elapsed > _options.EffectiveStepTimeout)
            {
                return false;
            }

            if (poll >= SpinPollsBeforeYield)
            {
                Thread.Yield();
            }
        }

        return false;
    }
}
