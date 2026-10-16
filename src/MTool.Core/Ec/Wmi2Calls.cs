using System.Collections.ObjectModel;

namespace MTool.Core.Ec;

/// <summary>
/// One <c>MSI_ACPI</c> method call M-Tool may make: a read method and the first byte of its packet.
/// Only <see cref="Wmi2Calls"/> creates one, so a request outside its lists cannot exist.
/// </summary>
public sealed record Wmi2Request
{
    internal Wmi2Request(string method, byte sub) => (Method, Sub) = (method, sub);

    public string Method { get; }

    /// <summary>The first byte of the 32-byte input packet; for <c>Get_Data</c> the EC address.</summary>
    public byte Sub { get; }
}

/// <summary>
/// Every WMI2 call M-Tool may make (CLAUDE.md, "WMI2 canlı okuma"): <c>Get_*</c> methods only, and
/// <c>Get_Data</c> only at <see cref="DataRegisters"/>. Widening a list needs Kutay's approval.
/// </summary>
public static class Wmi2Calls
{
    public const string GetData = "Get_Data";

    /// <summary>
    /// Read on every live poll: temperature CPU/GPU, fan percent CPU/GPU, tach CPU 0xC8:0xC9 and GPU
    /// 0xCA:0xCB (single byte or big-endian pair, by model). B family; GhostDeck 7f3a670 (48 tested
    /// models), msi-ec, YAMDCC: docs/plans/asama10.md §1.6.
    /// </summary>
    public static IReadOnlyList<byte> SensorRegisters { get; } = ReadOnly<byte>([0x68, 0x80, 0x71, 0x89, 0xC8, 0xC9, 0xCA, 0xCB]);

    /// <summary>Read on opening and on "Refresh" only: Cooler Boost, performance mode, fan mode, charge limit.</summary>
    public static IReadOnlyList<byte> StatusRegisters { get; } = ReadOnly<byte>([0x98, 0xD2, 0xD4, 0xD7]);

    public static IReadOnlyList<byte> DataRegisters { get; } = ReadOnly([.. SensorRegisters, .. StatusRegisters]);

    /// <summary>The report's raw packets (<c>--report --wmi2</c> and the GUI paths), in order.</summary>
    public static IReadOnlyList<Wmi2Request> Report { get; } = ReadOnly<Wmi2Request>(
    [
        new("Get_WMI", 0),
        new("Get_EC", 0),
        new("Get_Temperature", 0),
        new("Get_Temperature", 1),
        new("Get_Temperature", 2),
        new("Get_Fan", 0),
        new("Get_Fan", 1),
        new("Get_Fan", 2),
        new("Get_Thermal", 1),
        new("Get_Thermal", 2),
        new("Get_AP", 0),
        new("Get_AP", 1),
    ]);

    /// <summary>The only list read periodically.</summary>
    public static IReadOnlyList<Wmi2Request> Live { get; } = DataCalls(SensorRegisters);

    public static IReadOnlyList<Wmi2Request> Status { get; } = DataCalls(StatusRegisters);

    public static IReadOnlyList<Wmi2Request> Allowed { get; } = ReadOnly([.. Report, .. Live, .. Status]);

    /// <summary>The <c>Get_Data</c> call for a register in <see cref="DataRegisters"/>.</summary>
    /// <exception cref="InvalidOperationException">The register is not in the list.</exception>
    public static Wmi2Request DataRequest(byte register) =>
        Allowed.FirstOrDefault(r => r.Method == GetData && r.Sub == register)
        ?? throw new InvalidOperationException($"0x{register:X2} is not a WMI2 register M-Tool may read.");

    /// <summary>Exact match against <see cref="Allowed"/>: the method name is compared ordinally.</summary>
    public static bool IsAllowed(string method, byte sub) => Allowed.Any(r => r.Method == method && r.Sub == sub);

    private static IReadOnlyList<Wmi2Request> DataCalls(IReadOnlyList<byte> registers) =>
        ReadOnly(registers.Select(register => new Wmi2Request(GetData, register)).ToArray());

    private static ReadOnlyCollection<T> ReadOnly<T>(T[] items) => Array.AsReadOnly(items);
}
