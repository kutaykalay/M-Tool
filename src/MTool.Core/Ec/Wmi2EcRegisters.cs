namespace MTool.Core.Ec;

/// <summary>
/// EC registers read through WMI2 <c>Get_Data</c>, one call per register, and only at
/// <see cref="Wmi2Calls.DataRegisters"/>. Read-only by type: it is not an
/// <see cref="IEcWritableRegisters"/>, so <see cref="EcWorker"/> refuses a write through it.
/// </summary>
public sealed class Wmi2EcRegisters(IWmi2Methods wmi) : IEcRegisters
{
    private const int StatusByte = 0;
    private const int ValueByte = 1;
    private const byte Success = 1;
    private const int PacketLength = 32;
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

    private byte Call(Wmi2Request request)
    {
        var name = $"{request.Method}(0x{request.Sub:X2})";
        byte[]? answer;
        try
        {
            answer = wmi.Call(request);
        }
        catch (Exception ex) when (ex is not EcAccessException and not OutOfMemoryException)
        {
            throw new EcAccessException($"WMI2 {name} failed: {ex.Message}", ex);
        }

        // GhostDeck docs/TECHNICAL.md §5.3: byte 0 is the OK flag (01), byte 1 the value.
        return answer is { Length: PacketLength } && answer[StatusByte] == Success
            ? answer[ValueByte]
            : throw new EcAccessException(answer is null
                ? $"WMI2 {name} answered nothing."
                : $"WMI2 {name} answered {answer.Length} bytes, status {(answer.Length > 0 ? answer[StatusByte] : -1)}: not a value.");
    }
}
