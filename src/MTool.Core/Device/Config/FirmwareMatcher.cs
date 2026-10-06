namespace MTool.Core.Device.Config;

public enum MatchKind
{
    /// <summary>No record knows this firmware.</summary>
    None,

    /// <summary>The firmware is in a record's exact list.</summary>
    Exact,

    /// <summary>The firmware shares a record's family (<c>xxxxbMSn.y</c>) but is not in its exact list.</summary>
    Family,
}

/// <summary>Which device record a firmware matched. Informational: writes still follow <see cref="FirmwareInfo.IsSupported"/>.</summary>
public sealed record DeviceMatch(MatchKind Kind, string? RecordId, string? DisplayName)
{
    public static DeviceMatch None { get; } = new(MatchKind.None, null, null);

    public override string ToString() => Kind == MatchKind.None ? "eşleşme yok" : $"{Kind} ({RecordId})";
}

/// <summary>
/// Finds the record for a firmware: an exact list first, then a family. A family match never means
/// the record's values were checked on this firmware.
/// </summary>
public static class FirmwareMatcher
{
    public static DeviceMatch Match(FirmwareInfo? firmware, IReadOnlyList<DeviceConfig> catalog)
    {
        if (firmware?.Version is not { Length: > 0 } version)
        {
            return DeviceMatch.None;
        }

        if (catalog.FirstOrDefault(c => c.Firmware.Exact.Contains(version, StringComparer.Ordinal)) is { } exact)
        {
            return new DeviceMatch(MatchKind.Exact, exact.Id, exact.DisplayName);
        }

        var family = version.Length >= DeviceConfigValidator.FamilyLength
            ? catalog.FirstOrDefault(c => c.Firmware.Family is { } f && version.StartsWith(f, StringComparison.Ordinal))
            : null;
        return family is null ? DeviceMatch.None : new DeviceMatch(MatchKind.Family, family.Id, family.DisplayName);
    }
}
