using System.Diagnostics;

namespace MTool.Core.Ec;

/// <summary>
/// ACPI EC read protocol (ACPI spec ch. 12) over raw ports. Windows' own EC driver can interleave
/// with us, so every step waits on the status flags, an answer is rejected when another byte is
/// already queued behind it, and after a failed attempt the output buffer is drained until it
/// stays empty. Not thread-safe: use it only from <see cref="EcWorker"/>.
/// </summary>
public sealed class EcController : IEcRegisters
{
    private const byte DataPort = 0x62;
    private const byte CommandPort = 0x66;
    private const byte ReadCommand = 0x80;
    private const byte OutputBufferFull = 0x01;
    private const byte InputBufferFull = 0x02;
    private const int SpinPollsBeforeYield = 20;
    private const int RegisterCount = 256;

    private readonly IPortIo _ports;
    private readonly EcProtocolOptions _options;
    private int _recoveredFailures;

    public EcController(IPortIo ports, EcProtocolOptions? options = null)
    {
        _ports = ports;
        _options = options ?? EcProtocolOptions.Default;
    }

    /// <summary>Failed attempts that a later retry recovered from (diagnostics, safe to read from any thread).</summary>
    public int RecoveredFailures => Volatile.Read(ref _recoveredFailures);

    public byte Read(byte register)
    {
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            if (TryRead(register, out var value))
            {
                Interlocked.Add(ref _recoveredFailures, attempt - 1);
                return value;
            }

            Settle();
        }

        throw new EcAccessException(
            $"EC register 0x{register:X2} could not be read after {_options.MaxAttempts} attempts.");
    }

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

    private bool TryRead(byte register, out byte value)
    {
        value = 0;
        if (OutputPending())
        {
            return false;
        }

        if (!WaitUntil(InputBufferFull, set: false))
        {
            return false;
        }

        _ports.Out(CommandPort, ReadCommand);
        if (!WaitUntil(InputBufferFull, set: false))
        {
            return false;
        }

        _ports.Out(DataPort, register);
        if (!WaitUntil(OutputBufferFull, set: true))
        {
            return false;
        }

        value = _ports.In(DataPort);

        // Another byte already waiting means the stream is out of step: we cannot tell whether
        // the byte we took was ours or a late/foreign one.
        return !OutputPending();
    }

    private bool OutputPending() => (_ports.In(CommandPort) & OutputBufferFull) != 0;

    /// <summary>Drains the output buffer until it has stayed empty for the configured number of polls.</summary>
    private void Settle()
    {
        var clearPolls = 0;
        var pollBudget = _options.MaxStatusPolls + _options.SettleClearPolls;
        for (var poll = 0; poll < pollBudget && clearPolls < _options.SettleClearPolls; poll++)
        {
            if (OutputPending())
            {
                _ports.In(DataPort);
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
        for (var poll = 0; poll < _options.MaxStatusPolls; poll++)
        {
            var isSet = (_ports.In(CommandPort) & flag) != 0;
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
