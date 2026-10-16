using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

/// <summary>
/// A scripted <c>MSI_ACPI</c>: <c>Get_Data(address)</c> answers a well-formed packet with the byte at
/// <see cref="this[byte]"/>; every call is recorded. Never touches WMI.
/// </summary>
internal sealed class FakeWmi2Methods : IWmi2Methods
{
    private readonly byte[] _memory = new byte[256];

    public List<Wmi2Request> Calls { get; } = [];

    /// <summary>Replaces the whole answer of a call (short packet, failure flag...); null = the default.</summary>
    public Func<Wmi2Request, byte[]?>? AnswerHook { get; set; }

    /// <summary>When set, every call throws this.</summary>
    public Exception? Error { get; set; }

    public byte this[byte register]
    {
        get => _memory[register];
        set => _memory[register] = value;
    }

    public byte[] Call(Wmi2Request request)
    {
        Calls.Add(request);
        if (Error is { } error)
        {
            throw error;
        }

        return AnswerHook?.Invoke(request) ?? Packet(request.Method == Wmi2Calls.GetData ? _memory[request.Sub] : (byte)0);
    }

    /// <summary>32 bytes, success flag 1, the value in byte 1 (GhostDeck docs/TECHNICAL.md §5.3).</summary>
    public static byte[] Packet(byte value, byte status = 1, int length = 32)
    {
        var packet = new byte[length];
        if (length > 0)
        {
            packet[0] = status;
        }

        if (length > 1)
        {
            packet[1] = value;
        }

        return packet;
    }
}
