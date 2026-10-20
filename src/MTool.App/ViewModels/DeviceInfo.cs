using System.Text;
using MTool.App.Resources;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.App.ViewModels;

/// <summary>One row of the About tab's device section.</summary>
public sealed record DeviceInfoLine(string Label, string Value);

/// <summary>
/// The device section of the About tab. Only what M-Tool already has: the SMBIOS fields the report reads
/// (never a serial number or a user name), the firmware and record found at start-up, the session's
/// access and write mode. Nothing here reads the EC.
/// </summary>
internal static class DeviceInfo
{
    /// <param name="system">Null when SMBIOS could not be read.</param>
    /// <param name="pawnIo">The installed PawnIO version; null when unknown.</param>
    public static IReadOnlyList<DeviceInfoLine> Lines(SystemInfo? system, DeviceAccess access, Version? pawnIo) =>
    [
        new(Strings.About_Device_Model, Join(' ', system?.Manufacturer, system?.Model)),
        new(Strings.About_Device_Bios, WithDate(system?.BiosVersion, system?.BiosDate)),
        new(Strings.About_Device_Cpu, Join(' ', system?.Cpu)),
        new(Strings.About_Device_Windows, Join(' ', system?.Windows)),
        new(Strings.About_Device_Firmware, WithDate(access.Firmware?.Version, access.Firmware?.Date)),
        new(Strings.About_Device_Record, Record(access.Match)),
        new(Strings.About_Device_EcAccess, access.PortFeaturesAvailable
            ? string.Format(Strings.About_Access_WmiAndPort, pawnIo?.ToString() ?? Strings.About_Unknown)
            : Strings.About_Access_WmiOnly),
        new(Strings.About_Device_Writes, access.WriteMode switch
        {
            WriteMode.Enabled => Strings.About_Writes_Enabled,
            WriteMode.DryRun => Strings.About_Writes_DryRun,
            _ => Strings.About_Writes_Locked,
        }),
    ];

    /// <summary>What "Copy" puts on the clipboard: the M-Tool version, then one "label: value" line per row.</summary>
    public static string ToText(string appVersion, IReadOnlyList<DeviceInfoLine> lines) =>
        string.Join(Environment.NewLine, [$"M-Tool {appVersion}", .. lines.Select(l => $"{l.Label}: {l.Value}")]);

    private static string Record(DeviceMatch? match) => match switch
    {
        null => Strings.About_Unknown,
        { Kind: MatchKind.None } => Strings.About_Match_None,
        { Kind: MatchKind.Exact } => $"{Clean(match.DisplayName) ?? match.RecordId} ({Strings.About_Match_Exact})",
        _ => $"{Clean(match.DisplayName) ?? match.RecordId} ({Strings.About_Match_Family})",
    };

    private static string WithDate(string? value, string? date) =>
        (Clean(value), Clean(date)) switch
        {
            (null, _) => Strings.About_Unknown,
            ({ } v, null) => v,
            ({ } v, { } d) => $"{v} ({d})",
        };

    private static string Join(char separator, params string?[] parts)
    {
        var kept = parts.Select(Clean).OfType<string>().ToArray();
        return kept.Length == 0 ? Strings.About_Unknown : string.Join(separator, kept);
    }

    /// <summary>One line, no control or direction characters (SMBIOS text is the vendor's), trimmed; null when nothing is left.</summary>
    private static string? Clean(string? value)
    {
        var text = new StringBuilder();
        var lastWasSpace = true; // also trims the start
        foreach (var rune in (value ?? "").EnumerateRunes())
        {
            if (rune == Rune.ReplacementChar || PrivateText.IsUnprintable(rune))
            {
                continue;
            }

            var isSpace = Rune.IsWhiteSpace(rune);
            if (!(isSpace && lastWasSpace))
            {
                text.Append(isSpace ? " " : rune.ToString());
            }

            lastWasSpace = isSpace;
        }

        var cleaned = text.ToString().TrimEnd();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
