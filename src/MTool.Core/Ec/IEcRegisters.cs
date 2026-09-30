namespace MTool.Core.Ec;

/// <summary>Read access to EC memory. Writes go only through the gateway (stage 2).</summary>
public interface IEcRegisters
{
    byte Read(byte register);

    IReadOnlyList<byte> ReadBlock(byte startRegister, int count);
}
