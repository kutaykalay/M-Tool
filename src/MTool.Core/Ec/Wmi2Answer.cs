namespace MTool.Core.Ec;

/// <summary>
/// The one check of an <c>MSI_ACPI</c> answer, for reads and writes alike: 32 bytes and the OK flag in byte 0
/// (GhostDeck docs/TECHNICAL.md §5.3). Any other answer, and any failure of the call, is an <see cref="EcAccessException"/>.
/// </summary>
internal static class Wmi2Answer
{
    public const int ValueByte = 1;

    private const int StatusByte = 0;
    private const byte Success = 1;
    private const int PacketLength = 32;

    /// <summary>The well-formed success packet <paramref name="call"/> answered.</summary>
    /// <param name="name">The call as the error names it, e.g. <c>Get_Data(0x68)</c>.</param>
    /// <exception cref="EcAccessException">The call failed, or its answer is not a well-formed success.</exception>
    public static byte[] Of(string name, Func<byte[]?> call)
    {
        byte[]? answer;
        try
        {
            answer = call();
        }
        catch (Exception ex) when (ex is not EcAccessException and not OutOfMemoryException)
        {
            throw new EcAccessException($"WMI2 {name} failed: {ex.Message}", ex);
        }

        return answer is { Length: PacketLength } && answer[StatusByte] == Success
            ? answer
            : throw new EcAccessException(answer is null
                ? $"WMI2 {name} answered nothing."
                : $"WMI2 {name} answered {answer.Length} bytes, status {(answer.Length > 0 ? answer[StatusByte] : -1)}: not a success packet.");
    }
}
