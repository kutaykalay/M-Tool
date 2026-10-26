namespace MTool.Core.Ec;

/// <summary>
/// EC registers for a WMI2 experimental write (CLAUDE.md, "WMI2 deneysel yazma"): reads are
/// <see cref="Wmi2EcRegisters"/> (<c>Get_Data</c> at <see cref="Wmi2Calls.DataRegisters"/> only), a write is one
/// <c>Set_Data</c> whose request <see cref="Wmi2Writes"/> made, so a register or byte outside the ceiling never
/// reaches WMI. It does not read back: <see cref="EcGateway"/> verifies every write.
/// </summary>
public sealed class Wmi2WritableRegisters(IWmi2Methods wmi, IWmi2DataWriter writer) : IEcRegisters, IEcWritableRegisters
{
    private readonly Wmi2EcRegisters _reader = new(wmi);

    /// <inheritdoc cref="Wmi2EcRegisters.Read"/>
    public byte Read(byte register) => _reader.Read(register);

    /// <inheritdoc cref="Wmi2EcRegisters.ReadBlock"/>
    public IReadOnlyList<byte> ReadBlock(byte startRegister, int count) => _reader.ReadBlock(startRegister, count);

    /// <exception cref="InvalidOperationException">The register or the byte is outside the ceiling; nothing was called.</exception>
    /// <exception cref="EcAccessException">The call failed, or its answer is not a well-formed success.</exception>
    void IEcWritableRegisters.Write(byte register, byte value)
    {
        var request = Wmi2Writes.Request(register, value);
        Wmi2Answer.Of($"{Wmi2Writes.SetData}(0x{register:X2})", () => writer.SetData(request));
    }
}
