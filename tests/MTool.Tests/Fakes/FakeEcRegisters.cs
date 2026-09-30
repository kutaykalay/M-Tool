using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

internal sealed class FakeEcRegisters : IEcRegisters
{
    private readonly byte[] _memory = new byte[256];

    public void Load(byte startRegister, params byte[] values) =>
        values.CopyTo(_memory, startRegister);

    public void LoadAscii(byte startRegister, string text) =>
        Load(startRegister, System.Text.Encoding.ASCII.GetBytes(text));

    public byte Read(byte register) => _memory[register];

    public IReadOnlyList<byte> ReadBlock(byte startRegister, int count) =>
        _memory.AsSpan(startRegister, count).ToArray();
}
