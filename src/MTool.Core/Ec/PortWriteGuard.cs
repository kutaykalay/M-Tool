using MTool.Core.Device;

namespace MTool.Core.Ec;

/// <summary>
/// Catches a port access that disturbed other EC memory, as the port handshake racing Windows' ACPI
/// driver did in stage 4 (0x6A and 0xF2 mangled). Before the first port access of a Cooler Boost or
/// charge limit plan, the registers that matter most and that WMI can read (both fan tables,
/// performance mode, fan mode) are read; after the plan, whether it succeeded or failed, they are
/// read again and compared. Through <see cref="RoutedEcRegisters"/> these reads go to WMI, so the
/// check itself never uses the port. A change made legitimately in that window (an Fn key, MSI
/// software) also counts: the gateway would rather lock than guess.
/// </summary>
internal static class PortWriteGuard
{
    private static readonly IReadOnlyList<byte> Watched = BuildWatched();

    public static bool Guards(WritePlan plan) => plan.Writes.Any(w => WmiMap.PortRegisters.Contains(w.Register));

    public static IReadOnlyDictionary<byte, byte> Snapshot(IEcRegisters ec) =>
        Watched.ToDictionary(r => r, ec.Read);

    /// <summary>Compares against <paramref name="before"/>. Never throws: an unreadable EC counts as disturbed.</summary>
    public static PortDisturbance Check(IEcRegisters ec, IReadOnlyDictionary<byte, byte> before)
    {
        try
        {
            var after = Snapshot(ec);
            return new PortDisturbance(
                Watched.Where(r => before[r] != after[r]).Select(r => new RegisterWrite(r, after[r])).ToArray(),
                Unreadable: false);
        }
        catch (Exception)
        {
            return new PortDisturbance([], Unreadable: true);
        }
    }

    private static byte[] BuildWatched()
    {
        var tables = new[] { EcMap.CpuFan, EcMap.GpuFan }.SelectMany(fan =>
            Range(fan.UpThresholdsStart, EcMap.ThresholdCount).Concat(Range(fan.SpeedsStart, EcMap.SpeedCount)));
        return [.. tables.Append(EcMap.PerformanceMode).Append(EcMap.FanMode).Order()];
    }

    private static IEnumerable<byte> Range(byte start, int count) =>
        Enumerable.Range(start, count).Select(r => (byte)r);
}

/// <param name="Changes">Watched registers that changed, with their new value, in register order.</param>
/// <param name="Unreadable">The registers could not be read afterwards, so nothing can be ruled out.</param>
internal sealed record PortDisturbance(IReadOnlyList<RegisterWrite> Changes, bool Unreadable)
{
    public bool Any => Unreadable || Changes.Count > 0;

    public bool TouchesFanTable => Unreadable || Changes.Any(c => EcWriteRules.IsFanTable(c.Register));

    public string Describe() =>
        Unreadable ? "İzlenen register'lar port erişiminden sonra okunamadı."
        : Changes.Count > 0 ? $"Port erişimi başka register'ları değiştirdi: {string.Join(' ', Changes)}."
        : "";
}
