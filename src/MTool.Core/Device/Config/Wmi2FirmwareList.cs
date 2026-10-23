using System.Text.Json;
using System.Text.RegularExpressions;
using MTool.Core.Ec;

namespace MTool.Core.Device.Config;

/// <summary>
/// The WMI2 firmware msi-ec knows (plan asama15 §4.1-4.2), from <c>tools/msiec-import</c>: exact strings
/// (experimental writes may be offered), families (read-only match) and per-family performance bytes.
/// Not a <see cref="DeviceConfig"/>: addresses and the write ceiling live in code
/// (<see cref="Wmi2Family"/>, <see cref="Wmi2WriteCeiling"/>), and nothing here widens them.
/// Use <see cref="Load"/>: a list that has not passed <see cref="Validate"/> must not be used.
/// </summary>
/// <param name="FamilyOverrides">Family → mode → the code's exception byte for that mode (<see cref="Wmi2WriteCeiling.PerformanceExceptions"/>).</param>
public sealed partial record Wmi2FirmwareList(
    int SchemaVersion,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Exact,
    IReadOnlyList<string> Families,
    IReadOnlyDictionary<string, IReadOnlyDictionary<PerformanceMode, byte>> FamilyOverrides)
{
    public const int SupportedSchemaVersion = 1;

    /// <summary>The EC firmware string: <c>xxxxbMSn.yzz</c>.</summary>
    public const int FirmwareLength = 12;

    // Uppercase ASCII letters and digits around the dot (158NIMS1.10D and 15FMIBA1.102 are real);
    // \z, not $: $ also matches before a final line break.
    [GeneratedRegex(@"^[0-9A-Z]{8}\.[0-9A-Z]{3}\z")]
    private static partial Regex FirmwarePattern();

    [GeneratedRegex(@"^[0-9A-Z]{8}\.[0-9A-Z]\z")]
    private static partial Regex FamilyPattern();

    /// <summary>The embedded <c>Devices/msi-wmi2-firmware.json</c>; outside <see cref="DeviceConfigLoader.ResourcePrefix"/> on purpose.</summary>
    public const string ResourceName = "MTool.Wmi2Firmware.json";

    /// <summary>Loads the list built into M-Tool.</summary>
    /// <param name="catalog">The loaded device records: no WMI2 firmware or family may overlap theirs.</param>
    /// <exception cref="InvalidDataException">The resource is missing or breaks a rule.</exception>
    /// <exception cref="JsonException">The resource is not a single, complete list.</exception>
    public static Wmi2FirmwareList LoadEmbedded(IReadOnlyList<DeviceConfig> catalog)
    {
        using var stream = typeof(Wmi2FirmwareList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd(), catalog);
    }

    /// <summary>Parses and validates the list.</summary>
    /// <param name="catalog">The loaded device records: no WMI2 firmware or family may overlap theirs.</param>
    /// <exception cref="JsonException">The text is not a single, complete list.</exception>
    /// <exception cref="InvalidDataException">The list breaks a rule; the message names every problem.</exception>
    public static Wmi2FirmwareList Load(string json, IReadOnlyList<DeviceConfig> catalog)
    {
        var list = Parse(json);
        var errors = Validate(list, catalog);
        return errors.Count == 0 ? list : throw new InvalidDataException($"WMI2 firmware list invalid: {string.Join(" ", errors)}");
    }

    /// <summary>Same strict rules as device records; no validation.</summary>
    /// <exception cref="JsonException">The text is not a single, complete list.</exception>
    internal static Wmi2FirmwareList Parse(string json) =>
        JsonSerializer.Deserialize<Wmi2FirmwareList>(json, DeviceConfigLoader.Options) ?? throw new JsonException("The list is empty (null).");

    public MatchKind Match(FirmwareInfo? firmware)
    {
        if (firmware?.Version is not { Length: > 0 } version)
        {
            return MatchKind.None;
        }

        if (Exact.Contains(version, StringComparer.Ordinal))
        {
            return MatchKind.Exact;
        }

        return FamilyOf(version) is { } family && Families.Contains(family, StringComparer.Ordinal) ? MatchKind.Family : MatchKind.None;
    }

    /// <summary>
    /// The byte to write for <paramref name="mode"/>: the family's override, else the standard byte;
    /// null unless the firmware is an <see cref="MatchKind.Exact"/> match (a family match is read-only).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined mode.</exception>
    /// <exception cref="InvalidOperationException">The list holds an override the code does not allow (it was never validated).</exception>
    public byte? PerformanceByte(FirmwareInfo? firmware, PerformanceMode mode)
    {
        if (!Wmi2WriteCeiling.StandardPerformance.TryGetValue(mode, out var standard))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a performance mode the WMI2 ceiling knows.");
        }

        if (Match(firmware) != MatchKind.Exact)
        {
            return null;
        }

        if (!FamilyOverrides.TryGetValue(FamilyOf(firmware!.Version)!, out var modes) || !modes.TryGetValue(mode, out var value))
        {
            return standard;
        }

        // Checked again here: the write path must not depend on Validate having run.
        return Wmi2WriteCeiling.IsPerformanceException(mode, value)
            ? value
            : throw new InvalidOperationException($"Override 0x{value:X2} for {mode} is not allowed by the code.");
    }

    /// <summary>Returns the problems (English, for the log); empty when the list is valid.</summary>
    /// <param name="catalog">The loaded device records: no WMI2 firmware or family may overlap theirs.</param>
    public static IReadOnlyList<string> Validate(Wmi2FirmwareList list, IReadOnlyList<DeviceConfig> catalog)
    {
        if (HasNullParts(list))
        {
            return ["The list has an empty (null) item."];
        }

        var errors = new List<string>();
        if (list.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add($"schemaVersion {list.SchemaVersion} is not supported (expected {SupportedSchemaVersion}).");
        }

        if (list.Sources.Count == 0 || list.Sources.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("sources must name where the list came from.");
        }

        CheckFirmware(list, errors);
        CheckRecords(list, catalog, errors);
        CheckOverrides(list, errors);
        return errors.AsReadOnly();
    }

    private static string? FamilyOf(string firmware) =>
        firmware.Length >= DeviceConfigValidator.FamilyLength ? firmware[..DeviceConfigValidator.FamilyLength] : null;

    /// <summary>Collection elements can still be null after JSON reading; nothing else may run on them.</summary>
    private static bool HasNullParts(Wmi2FirmwareList l) =>
        l.Sources is null || l.Sources.Any(s => s is null)
        || l.Exact is null || l.Exact.Any(f => f is null)
        || l.Families is null || l.Families.Any(f => f is null)
        || l.FamilyOverrides is null || l.FamilyOverrides.Values.Any(m => m is null);

    private static void CheckFirmware(Wmi2FirmwareList list, List<string> errors)
    {
        if (list.Exact.Count == 0)
        {
            errors.Add("exact cannot be empty.");
        }

        foreach (var firmware in list.Exact)
        {
            CheckText("exact", firmware, FirmwareLength, FirmwarePattern(), errors);
            if (firmware.Length == FirmwareLength && FamilyOf(firmware) is { } family && !list.Families.Contains(family, StringComparer.Ordinal))
            {
                errors.Add($"firmware \"{ConfigText.Show(firmware)}\" is not in the families.");
            }
        }

        foreach (var family in list.Families)
        {
            CheckText("families", family, DeviceConfigValidator.FamilyLength, FamilyPattern(), errors);
            if (!list.Exact.Any(f => f.StartsWith(family, StringComparison.Ordinal)))
            {
                errors.Add($"family \"{ConfigText.Show(family)}\" has no exact firmware.");
            }
        }

        errors.AddRange(Duplicates(list.Exact).Select(f => $"firmware \"{ConfigText.Show(f)}\" is listed twice."));
        errors.AddRange(Duplicates(list.Families).Select(f => $"family \"{ConfigText.Show(f)}\" is listed twice."));
    }

    /// <summary>An exact match opens writes, so only the firmware string shape MSI uses is accepted.</summary>
    private static void CheckText(string field, string text, int length, Regex pattern, List<string> errors)
    {
        if (ConfigText.HasControl(text))
        {
            errors.Add($"{field} cannot contain control characters.");
        }

        if (text.Length != length)
        {
            errors.Add($"{field} \"{ConfigText.Show(text)}\" must be {length} characters.");
        }
        else if (!pattern.IsMatch(text))
        {
            errors.Add($"{field} \"{ConfigText.Show(text)}\" does not fit the pattern (uppercase letters and digits, a dot).");
        }
    }

    /// <summary>
    /// A firmware the device records know, exactly or by family, keeps going to them. The code's
    /// write-verified firmware counts even when its record did not load.
    /// </summary>
    private static void CheckRecords(Wmi2FirmwareList list, IReadOnlyList<DeviceConfig> catalog, List<string> errors)
    {
        var verified = DeviceConfigValidator.WriteVerifiedFirmware;
        var recordExact = catalog.SelectMany(c => c.Firmware.Exact).Concat(verified).ToHashSet(StringComparer.Ordinal);
        var recordFamilies = catalog.SelectMany(c => c.Firmware.Families)
            .Concat(verified.Select(FamilyOf).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var firmware in list.Exact)
        {
            if (recordExact.Contains(firmware))
            {
                errors.Add($"firmware \"{ConfigText.Show(firmware)}\" is listed by a device record.");
            }
            else if (FamilyOf(firmware) is { } family && recordFamilies.Contains(family))
            {
                errors.Add($"firmware \"{ConfigText.Show(firmware)}\" is in a device record's family.");
            }
        }

        foreach (var family in list.Families)
        {
            if (recordFamilies.Contains(family))
            {
                errors.Add($"family \"{ConfigText.Show(family)}\" is a device record's family.");
            }

            foreach (var firmware in recordExact.Where(f => f.StartsWith(family, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                errors.Add($"family \"{ConfigText.Show(family)}\" holds device record firmware \"{ConfigText.Show(firmware)}\".");
            }
        }
    }

    private static void CheckOverrides(Wmi2FirmwareList list, List<string> errors)
    {
        foreach (var (family, modes) in list.FamilyOverrides)
        {
            var shown = ConfigText.Show(family);
            if (!list.Families.Contains(family, StringComparer.Ordinal))
            {
                errors.Add($"familyOverrides \"{shown}\" is not in the families.");
            }

            if (modes.Count == 0)
            {
                errors.Add($"familyOverrides \"{shown}\" has no mode.");
            }

            foreach (var (mode, value) in modes.Where(m => !Wmi2WriteCeiling.IsPerformanceException(m.Key, m.Value)))
            {
                errors.Add($"familyOverrides \"{shown}\" {mode}: 0x{value:X2} is not the exception byte the code allows for this mode.");
            }
        }
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> items) =>
        items.GroupBy(i => i, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
}
