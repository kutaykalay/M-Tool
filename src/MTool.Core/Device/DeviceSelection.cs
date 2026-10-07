using MTool.Core.Device.Config;

namespace MTool.Core.Device;

/// <summary>
/// The record a session reads with, chosen from the firmware, and whether it may open the raw
/// port. The port opens only where writes can: an exact firmware of a record verified for writes
/// (Kutay, 7f). Everywhere else the session is WMI only, so an unverified EC never sees port traffic.
/// </summary>
/// <param name="Record">The matched record; the fallback when nothing matched.</param>
public sealed record DeviceSelection(DeviceConfig Record, DeviceMatch Match, bool PortAllowed)
{
    /// <param name="fallbackId">The record an unmatched firmware is read with.</param>
    /// <exception cref="InvalidOperationException">The fallback record is not in the catalog.</exception>
    public static DeviceSelection Choose(FirmwareInfo? firmware, IReadOnlyList<DeviceConfig> catalog, string fallbackId)
    {
        var match = FirmwareMatcher.Match(firmware, catalog);
        var record = match.Kind == MatchKind.None
            ? catalog.FirstOrDefault(c => c.Id == fallbackId)
                ?? throw new InvalidOperationException($"Geri düşülecek cihaz kaydı {fallbackId} yüklenemedi; ayrıntı log'da.")
            : catalog.FirstOrDefault(c => c.Id == match.RecordId)
                ?? throw new InvalidOperationException($"Eşleşen cihaz kaydı {match.RecordId} katalogda yok.");
        var portAllowed = match.Kind == MatchKind.Exact
            && firmware is { } read
            && record.Status == DeviceStatus.WriteVerified
            && DeviceConfigValidator.WriteVerifiedFirmware.Contains(read.Version);
        return new DeviceSelection(record, match, portAllowed);
    }
}
