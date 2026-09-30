using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

internal sealed class FakeEcRegisters : IEcWritableRegisters
{
    private readonly byte[] _memory = new byte[256];

    /// <summary>Registers that silently ignore writes (a write the EC never applied).</summary>
    public HashSet<byte> StuckRegisters { get; } = [];

    /// <summary>Writes to these registers are ignored only once, then succeed.</summary>
    public HashSet<byte> FlakyOnceRegisters { get; } = [];

    /// <summary>Every write attempt in order, including ignored ones.</summary>
    public List<(byte Register, byte Value)> Writes { get; } = [];

    /// <summary>Overrides what a read returns (e.g. garbage from concurrent EC traffic); null = memory.</summary>
    public Func<byte, byte?>? ReadHook { get; set; }

    /// <summary>When set, every access throws this (EC unreachable).</summary>
    public Exception? AccessError { get; set; }

    public byte this[byte register]
    {
        get => _memory[register];
        set => _memory[register] = value;
    }

    public void Load(byte startRegister, params byte[] values) =>
        values.CopyTo(_memory, startRegister);

    public void LoadAscii(byte startRegister, string text) =>
        Load(startRegister, System.Text.Encoding.ASCII.GetBytes(text));

    public byte Read(byte register)
    {
        ThrowIfUnreachable();
        return ReadHook?.Invoke(register) ?? _memory[register];
    }

    public IReadOnlyList<byte> ReadBlock(byte startRegister, int count)
    {
        ThrowIfUnreachable();
        return _memory.AsSpan(startRegister, count).ToArray();
    }

    public void Write(byte register, byte value)
    {
        ThrowIfUnreachable();
        Writes.Add((register, value));
        if (StuckRegisters.Contains(register) || FlakyOnceRegisters.Remove(register))
        {
            return;
        }

        _memory[register] = value;
    }

    private void ThrowIfUnreachable()
    {
        if (AccessError is { } error)
        {
            throw error;
        }
    }
}
