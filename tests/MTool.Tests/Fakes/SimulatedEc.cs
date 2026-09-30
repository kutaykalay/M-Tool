using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

/// <summary>
/// ACPI EC simulation behind ports 0x62/0x66: 256 bytes of memory plus the IBF/OBF handshake.
/// Responses queue up like a real output buffer, so a late answer to an abandoned read can land
/// in front of a newer one. Records protocol violations (writing while IBF is set, reading data
/// without OBF) so tests can prove the controller waits correctly.
/// </summary>
internal sealed class SimulatedEc : IPortIo
{
    private const byte DataPort = 0x62;
    private const byte CommandPort = 0x66;
    private const byte ReadCommand = 0x80;
    private const byte WriteCommand = 0x81;
    private const byte OutputBufferFull = 0x01;
    private const byte InputBufferFull = 0x02;

    private enum State { Idle, AwaitReadAddress, AwaitWriteAddress, AwaitWriteValue }

    private sealed class PendingOutput(byte value, int delayPolls)
    {
        public byte Value { get; } = value;
        public int DelayPolls { get; set; } = delayPolls;
    }

    private readonly byte[] _memory = new byte[256];
    private readonly Queue<PendingOutput> _output = new();
    private State _state = State.Idle;
    private int _ibfPollsLeft;
    private byte _pendingWriteAddress;
    private int _dropsLeft;
    private int? _nextResponseDelay;
    private byte? _injectOnce;
    private byte? _injectAlways;

    /// <summary>Status polls during which IBF stays set after each host write.</summary>
    public int BusyPolls { get; set; }

    /// <summary>Status polls before OBF is raised once a read address is accepted.</summary>
    public int ResponseDelayPolls { get; set; }

    /// <summary>When true IBF never clears (EC hung).</summary>
    public bool Hung { get; set; }

    public int ProtocolViolations { get; private set; }
    public int CompletedWrites { get; private set; }

    public byte this[byte register]
    {
        get => _memory[register];
        set => _memory[register] = value;
    }

    public void Load(byte startRegister, params byte[] values) =>
        values.CopyTo(_memory, startRegister);

    /// <summary>The next <paramref name="count"/> read transactions never raise OBF.</summary>
    public void DropNextReads(int count) => _dropsLeft = count;

    /// <summary>Only the next read's answer arrives after <paramref name="polls"/> status polls.</summary>
    public void DelayNextResponse(int polls) => _nextResponseDelay = polls;

    /// <summary>
    /// When the next read command arrives, a foreign byte (e.g. Windows' own EC traffic) lands in
    /// the output buffer ahead of our answer.
    /// </summary>
    public void InjectOutputOnNextReadCommand(byte value) => _injectOnce = value;

    public void InjectOutputOnEveryReadCommand(byte value) => _injectAlways = value;

    /// <summary>Leaves a stale byte in the output buffer, as if another host's read was not collected.</summary>
    public void LeaveStaleOutput(byte value) => _output.Enqueue(new PendingOutput(value, 0));

    public byte In(byte port)
    {
        if (port == CommandPort)
        {
            return ReadStatus();
        }

        if (port != DataPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (!OutputReady)
        {
            ProtocolViolations++;
            return 0;
        }

        return _output.Dequeue().Value;
    }

    public void Out(byte port, byte value)
    {
        if (IbfSet)
        {
            ProtocolViolations++;
        }

        _ibfPollsLeft = BusyPolls;

        if (port == CommandPort)
        {
            if (value == ReadCommand && (_injectOnce ?? _injectAlways) is { } foreign)
            {
                _injectOnce = null;
                _output.Enqueue(new PendingOutput(foreign, 0));
            }

            _state = value switch
            {
                ReadCommand => State.AwaitReadAddress,
                WriteCommand => State.AwaitWriteAddress,
                _ => State.Idle,
            };
            return;
        }

        if (port != DataPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        switch (_state)
        {
            case State.AwaitReadAddress:
                _state = State.Idle;
                AcceptReadAddress(value);
                break;
            case State.AwaitWriteAddress:
                _pendingWriteAddress = value;
                _state = State.AwaitWriteValue;
                break;
            case State.AwaitWriteValue:
                _memory[_pendingWriteAddress] = value;
                _state = State.Idle;
                CompletedWrites++;
                break;
            default:
                ProtocolViolations++;
                break;
        }
    }

    private bool IbfSet => Hung || _ibfPollsLeft > 0;

    private bool OutputReady => _output.Count > 0 && _output.Peek().DelayPolls == 0;

    private void AcceptReadAddress(byte register)
    {
        if (_dropsLeft > 0)
        {
            _dropsLeft--;
            return;
        }

        var delay = _nextResponseDelay ?? ResponseDelayPolls;
        _nextResponseDelay = null;
        _output.Enqueue(new PendingOutput(_memory[register], delay));
    }

    private byte ReadStatus()
    {
        byte status = 0;
        if (IbfSet)
        {
            status |= InputBufferFull;
            if (_ibfPollsLeft > 0)
            {
                _ibfPollsLeft--;
            }
        }

        if (_output.Count > 0 && _output.Peek().DelayPolls > 0)
        {
            _output.Peek().DelayPolls--;
        }

        if (OutputReady)
        {
            status |= OutputBufferFull;
        }

        return status;
    }
}
