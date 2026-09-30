namespace MTool.Core.Ec;

/// <summary>
/// Raw EC write access. Internal on purpose: outside this assembly the only way to write is
/// <see cref="EcGateway"/>, which validates and verifies every write.
/// </summary>
internal interface IEcWritableRegisters : IEcRegisters
{
    void Write(byte register, byte value);
}
