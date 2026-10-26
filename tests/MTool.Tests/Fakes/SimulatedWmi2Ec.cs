using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

/// <summary>
/// A simulated WMI2 EC: 256 bytes of memory behind <c>Get_Data</c> and <c>Set_Data</c>, with switches for
/// the ways a real one misbehaves (noisy bits, ignored or misplaced writes, errors, a hung call).
/// Every call is recorded; all state is guarded by a lock because calls may come from a worker thread.
/// Never touches WMI.
/// </summary>
internal sealed class SimulatedWmi2Ec : IWmi2Methods, IWmi2DataWriter, IDisposable
{
    private const int MemorySize = 256;
    private const byte CoolerBoostRegister = 0x98;
    private const byte CoolerBoostBit = 0x80;
    private const byte LowSevenBits = 0x7F;
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly byte[] _memory = new byte[MemorySize];
    private readonly List<Wmi2Request> _reads = [];
    private readonly List<Wmi2WriteRequest> _writes = [];
    private readonly ManualResetEventSlim _release = new(false);
    private bool _hung;

    /// <summary>Set when a call has been recorded and, with <see cref="Hung"/>, is about to block.</summary>
    public ManualResetEventSlim Entered { get; } = new(false);

    /// <summary>Every <c>Call</c> so far, as a copy.</summary>
    public IReadOnlyList<Wmi2Request> Reads
    {
        get
        {
            lock (_lock)
            {
                return [.. _reads];
            }
        }
    }

    /// <summary>Every <c>SetData</c> so far, as a copy; recorded before any behaviour, errors and hangs included.</summary>
    public IReadOnlyList<Wmi2WriteRequest> Writes
    {
        get
        {
            lock (_lock)
            {
                return [.. _writes];
            }
        }
    }

    /// <summary>Every <c>Get_Data(0x98)</c> first replaces the low 7 bits (a new value each read), keeping bit 7.</summary>
    public bool CoolerBoostNoise { get; set; }

    /// <summary>SetData answers success but memory does not change.</summary>
    public bool IgnoresWrites { get; set; }

    /// <summary>SetData stores the value at <c>Register + 1</c> instead of <c>Register</c>.</summary>
    public bool WritesNeighbour { get; set; }

    /// <summary>When it returns non-null, replaces the answer of a read (short packet, failure flag...).</summary>
    public Func<Wmi2Request, byte[]?>? ReadAnswer { get; set; }

    /// <summary>
    /// When it returns non-null, replaces the answer of a write. The memory effect still happens
    /// (unless <see cref="IgnoresWrites"/>), so it models "answered badly but wrote".
    /// </summary>
    public Func<Wmi2WriteRequest, byte[]?>? WriteAnswer { get; set; }

    /// <summary>When set, every call (read or write) throws this after being recorded.</summary>
    public Exception? Error { get; set; }

    /// <summary>While true, a call sets <see cref="Entered"/> and blocks until <see cref="Release"/> (10 s at most).</summary>
    public bool Hung
    {
        get
        {
            lock (_lock)
            {
                return _hung;
            }
        }

        set
        {
            lock (_lock)
            {
                _hung = value;
            }
        }
    }

    public byte this[byte register]
    {
        get
        {
            lock (_lock)
            {
                return _memory[register];
            }
        }

        set
        {
            lock (_lock)
            {
                _memory[register] = value;
            }
        }
    }

    /// <summary>Memory that fits a supported layout: 0x98 = 02, 0xD2 = C1, 0xD4 = 0D, 0xD7 = D0.</summary>
    public static SimulatedWmi2Ec Typical()
    {
        var ec = new SimulatedWmi2Ec();
        ec[CoolerBoostRegister] = 0x02;
        ec[0xD2] = 0xC1;
        ec[0xD4] = 0x0D;
        ec[0xD7] = 0xD0;
        return ec;
    }

    /// <summary>Copies <paramref name="values"/> into memory from <paramref name="startRegister"/>.</summary>
    public void Load(byte startRegister, params byte[] values)
    {
        lock (_lock)
        {
            values.CopyTo(_memory, startRegister);
        }
    }

    public byte[] Call(Wmi2Request request)
    {
        lock (_lock)
        {
            _reads.Add(request);
        }

        Gate();
        lock (_lock)
        {
            if (ReadAnswer?.Invoke(request) is { } answer)
            {
                return answer;
            }

            if (request.Method != Wmi2Calls.GetData)
            {
                return FakeWmi2Methods.Packet(0);
            }

            if (CoolerBoostNoise && request.Sub == CoolerBoostRegister)
            {
                var memory = _memory[CoolerBoostRegister];
                var next = (byte)(((memory & LowSevenBits) + 1) & LowSevenBits);
                _memory[CoolerBoostRegister] = (byte)((memory & CoolerBoostBit) | next);
            }

            return FakeWmi2Methods.Packet(_memory[request.Sub]);
        }
    }

    public byte[] SetData(Wmi2WriteRequest request)
    {
        lock (_lock)
        {
            _writes.Add(request);
        }

        Gate();
        lock (_lock)
        {
            if (!IgnoresWrites)
            {
                _memory[WritesNeighbour ? (byte)(request.Register + 1) : request.Register] = request.Value;
            }

            return WriteAnswer?.Invoke(request) ?? FakeWmi2Methods.Packet(0);
        }
    }

    /// <summary>Lets every blocked (and later) hung call proceed.</summary>
    public void Release() => _release.Set();

    public void Dispose()
    {
        Entered.Dispose();
        _release.Dispose();
    }

    // Blocks on a hang and throws Error; never holds the state lock while waiting.
    private void Gate()
    {
        if (Hung)
        {
            Entered.Set();
            if (!_release.Wait(HangLimit))
            {
                throw new TimeoutException("SimulatedWmi2Ec: hung call was never released.");
            }
        }

        if (Error is { } error)
        {
            throw error;
        }
    }
}
