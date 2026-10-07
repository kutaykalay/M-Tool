using System.Text;

namespace MTool.Core.Diagnostics;

/// <summary>One <c>MSI_ACPI</c> method call: its raw answer (a copy of its own), or why there is none.</summary>
public sealed record Wmi2Call(string Method, byte Sub, IReadOnlyList<byte>? Packet, string? Error)
{
    public IReadOnlyList<byte>? Packet { get; } = Packet is null ? null : Array.AsReadOnly(Packet.ToArray());
}

/// <summary>
/// The WMI2 calls a report makes, kept raw so the packet layout can be learned from reports.
/// Only <c>Get_*</c> methods, never <c>Set_*</c>. Opt-in (<c>--report --wmi2</c>): the methods run
/// firmware ACPI code, and that they have no side effects is an [assumption] (MSI Center and
/// YAMDCC call them all the time).
/// </summary>
public sealed record Wmi2Readout(IReadOnlyList<Wmi2Call> Calls)
{
    private const int FirmwareStart = 2;
    private const int FirmwareLength = 12;
    private const int DateStart = 14;
    private const int DateLength = 16;

    /// <summary>The fan period to RPM constant YAMDCC uses for WMI2 (the WMI1 EC uses another).</summary>
    private const int RpmConstant = 478_000;

    /// <summary>The fields YAMDCC (da7d9d8, Wmi2FanController) reads from these packets: [assumption], unverified.</summary>
    public Wmi2Reading Interpret()
    {
        var ec = Ok("Get_EC", 0);
        var temperatures = Ok("Get_Temperature", 0);
        var fans = Ok("Get_Fan", 0);
        return new Wmi2Reading(
            ec is null ? null : Ascii(ec, FirmwareStart, FirmwareLength),
            ec is null ? null : Ascii(ec, DateStart, DateLength),
            temperatures?[1],
            temperatures?[2],
            fans is null ? null : Rpm(fans[1], fans[2]),
            fans is null ? null : Rpm(fans[3], fans[4]));
    }

    /// <summary>A packet whose method reported success (first byte 1).</summary>
    private IReadOnlyList<byte>? Ok(string method, byte sub) =>
        Calls.FirstOrDefault(c => c.Method == method && c.Sub == sub)?.Packet is { } packet && packet[0] == Wmi2Probe.Success
            ? packet
            : null;

    /// <summary>The period is big-endian; 0 means the fan stands still.</summary>
    private static int Rpm(byte high, byte low) => (high << 8 | low) is var period and > 0 ? RpmConstant / period : 0;

    /// <summary>Control characters become '?': this text goes into a log and a public report.</summary>
    private static string Ascii(IReadOnlyList<byte> packet, int start, int length)
    {
        var text = new StringBuilder(length);
        for (var i = start; i < start + length; i++)
        {
            text.Append(packet[i] is >= 0x20 and < 0x7F ? (char)packet[i] : '?');
        }

        return text.ToString();
    }
}

/// <param name="CpuRpm">0 when the fan stands still; null when not read.</param>
public sealed record Wmi2Reading(string? Firmware, string? FirmwareDate, int? CpuTempC, int? GpuTempC, int? CpuRpm, int? GpuRpm);

public static class Wmi2Probe
{
    public const int PacketLength = 32;
    public const byte Success = 1;

    /// <summary>A method that hangs usually blocks the next ones too: after this many failures in a row the rest are skipped.</summary>
    public const int MaxFailuresInARow = 2;

    /// <summary>Every call, in order. Names starting with "Get_" only: a test pins it.</summary>
    public static IReadOnlyList<(string Method, byte Sub)> Calls { get; } = Array.AsReadOnly(new (string, byte)[]
    {
        ("Get_WMI", 0),
        ("Get_EC", 0),
        ("Get_Temperature", 0),
        ("Get_Temperature", 1),
        ("Get_Temperature", 2),
        ("Get_Fan", 0),
        ("Get_Fan", 1),
        ("Get_Fan", 2),
        ("Get_Thermal", 1),
        ("Get_Thermal", 2),
        ("Get_AP", 0),
        ("Get_AP", 1),
    });

    /// <param name="call">Calls one method with a packet whose first byte is <c>sub</c>; may throw.</param>
    public static Wmi2Readout Run(Func<string, byte, byte[]> call)
    {
        var results = new List<Wmi2Call>();
        var failuresInARow = 0;
        foreach (var (method, sub) in Calls)
        {
            if (failuresInARow == MaxFailuresInARow)
            {
                results.Add(new Wmi2Call(method, sub, null, $"skipped after {MaxFailuresInARow} failed calls in a row"));
                continue;
            }

            var result = Single(call, method, sub);
            failuresInARow = result.Packet is null ? failuresInARow + 1 : 0;
            results.Add(result);
        }

        return new Wmi2Readout(results.AsReadOnly());
    }

    private static Wmi2Call Single(Func<string, byte, byte[]> call, string method, byte sub)
    {
        try
        {
            var packet = call(method, sub);
            return packet.Length == PacketLength
                ? new Wmi2Call(method, sub, packet, null)
                : new Wmi2Call(method, sub, null, $"answer is {packet.Length} bytes, expected {PacketLength}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Wmi2Call(method, sub, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
