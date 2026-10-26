namespace MTool.Core.Ec;

/// <summary>
/// EC registers read through WMI2 <c>Get_Data</c>, one call per register, and only at
/// <see cref="Wmi2Calls.DataRegisters"/>. Read-only by type: it is not an
/// <see cref="IEcWritableRegisters"/>, so <see cref="EcWorker"/> refuses a write through it.
/// </summary>
public sealed class Wmi2EcRegisters(IWmi2Methods wmi) : IEcRegisters
{
    private const int AddressSpace = 0x100;

    /// <exception cref="InvalidOperationException">The register is not in the list; nothing was called.</exception>
    /// <exception cref="EcAccessException">The call failed, or its answer is not a well-formed success.</exception>
    public byte Read(byte register) => Call(Wmi2Calls.DataRequest(register));

    /// <summary>Every register is checked before the first call, so a block is read whole or not at all.</summary>
    public IReadOnlyList<byte> ReadBlock(byte startRegister, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startRegister + count, AddressSpace, nameof(count));
        var requests = Enumerable.Range(startRegister, count).Select(r => Wmi2Calls.DataRequest((byte)r)).ToArray();
        return Array.AsReadOnly(requests.Select(Call).ToArray());
    }

    private byte Call(Wmi2Request request) =>
        Wmi2Answer.Of($"{request.Method}(0x{request.Sub:X2})", () => wmi.Call(request))[Wmi2Answer.ValueByte];
}
