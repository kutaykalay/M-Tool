namespace MTool.Core.Ec;

/// <summary>Read access to EC memory. Writes go only through the gateway.</summary>
public interface IEcRegisters
{
    byte Read(byte register);

    IReadOnlyList<byte> ReadBlock(byte startRegister, int count);
}
