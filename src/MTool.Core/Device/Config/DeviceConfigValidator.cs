using System.Collections.Frozen;
using System.Text.RegularExpressions;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device.Config;

/// <summary>A record that cannot join the catalog because another one claims the same id, firmware or family.</summary>
public sealed record ConfigConflict(string Id, string Reason);

/// <summary>
/// Checks a <see cref="DeviceConfig"/> against the code's own ceilings, whatever the JSON says: table
/// shape, unique and reachable registers, limits, curves against the record's down offsets, the port
/// ceiling, and which firmware may be <see cref="DeviceStatus.WriteVerified"/>. A record with any
/// problem is not used. Feature registers and mode values are facts of the model, not a write
/// permission: writes stay behind <see cref="EcWriteRules"/>.
/// </summary>
public static partial class DeviceConfigValidator
{
    public const int SupportedSchemaVersion = 1;

    /// <summary><c>xxxxbMSn.y</c>: model, EC vendor, board and generation (msi-ec device support guide).</summary>
    public const int FamilyLength = 10;

    public const int MinDownOffsetC = 1;
    public const int MaxDownOffsetC = 15;

    private const int ThresholdCount = CurveValidator.PointCount - 1;
    private const int SpeedCount = CurveValidator.PointCount;
    private const int MaxBit = 7;
    private const int ChargeLimitEnableBit = 7;
    private const int RegisterSpace = 256;

    /// <summary>
    /// The second key for writes: only firmware listed here, in code, may be
    /// <see cref="DeviceStatus.WriteVerified"/>. A JSON change alone cannot open writes.
    /// </summary>
    public static IReadOnlySet<string> WriteVerifiedFirmware { get; } = new[] { EcMap.SupportedFirmware }.ToFrozenSet(StringComparer.Ordinal);

    // \z, not $: $ also matches before a final line break.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^MSI_[A-Za-z0-9_]+\z")]
    private static partial Regex WmiClassPattern();

    /// <summary>Returns human-readable (Turkish) problems; empty when the record is valid.</summary>
    public static IReadOnlyList<string> Validate(DeviceConfig config)
    {
        if (HasNullParts(config))
        {
            return ["Kayıtta boş (null) öğe var."];
        }

        var errors = new List<string>();
        CheckIdentity(config, errors);
        CheckFirmware(config, errors);
        CheckLimits(config.Limits, errors);
        CheckFans(config, errors);
        CheckFeatures(config.Features, errors);
        CheckRegistersUnique(config, errors);
        CheckWmi1(config, errors);
        CheckPort(config, errors);
        CheckPresets(config, errors);
        return errors.AsReadOnly();
    }

    /// <summary>Records that share an id, an exact firmware or a family with another record; each id once.</summary>
    public static IReadOnlyList<ConfigConflict> FindConflicts(IReadOnlyList<DeviceConfig> configs)
    {
        var sameId = configs.GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new ConfigConflict(g.Key, $"id \"{ConfigText.Show(g.Key)}\" birden çok kayıtta var."));

        var sameFirmware = Shared(configs.SelectMany(c => c.Firmware.Exact.Select(firmware => (Key: firmware, c.Id))), "firmware");
        var sameFamily = Shared(configs.Where(c => c.Firmware.Family is not null).Select(c => (Key: c.Firmware.Family!, c.Id)), "aile");

        return [.. sameId.Concat(sameFirmware).Concat(sameFamily).DistinctBy(c => c.Id)];
    }

    private static IEnumerable<ConfigConflict> Shared(IEnumerable<(string Key, string Id)> claims, string what) =>
        claims.GroupBy(x => x.Key, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.Id).Distinct().Count() > 1)
            .SelectMany(g => g.Select(x => new ConfigConflict(x.Id, $"{what} \"{ConfigText.Show(g.Key)}\" birden çok kayıtta var.")));

    /// <summary>Collection elements can still be null after JSON reading; nothing else may run on them.</summary>
    private static bool HasNullParts(DeviceConfig c) =>
        c.Firmware?.Exact is null || c.Firmware.Exact.Any(f => f is null)
        || c.FirmwareLocation is null || c.Limits is null
        || c.Sources is null || c.Sources.Any(s => s is null)
        || c.PortRegisters is null
        || HasNullFans(c.Fans) || HasNullFeatures(c.Features) || HasNullWmi(c.Wmi1) || HasNullPresets(c.Presets);

    private static bool HasNullFans(IReadOnlyList<FanConfig>? fans) =>
        fans is null || fans.Any(f => f?.UpThresholds is null || f.Speeds is null || f.FactoryCurve is { } curve && HasNullPoints(curve));

    private static bool HasNullFeatures(FeatureSet? features) =>
        features is null || features.PerformanceMode?.Modes is { } modes && modes.Any(m => m is null);

    private static bool HasNullWmi(Wmi1Layout? wmi) =>
        wmi is not null && (wmi.Fields is null || wmi.Fields.Any(f => f is null));

    private static bool HasNullPresets(IReadOnlyList<PresetConfig>? presets) =>
        presets is null || presets.Any(p => p?.Curves is null || p.Curves.Values.Any(c => c is null || HasNullPoints(c)));

    private static bool HasNullPoints(FanCurve curve) => curve.Points is null || curve.Points.Any(p => p is null);

    private static void CheckIdentity(DeviceConfig config, List<string> errors)
    {
        if (config.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add($"schemaVersion {config.SchemaVersion} desteklenmiyor (beklenen {SupportedSchemaVersion}).");
        }

        if (!IdPattern().IsMatch(config.Id))
        {
            errors.Add($"id \"{ConfigText.Show(config.Id)}\" küçük harf, rakam ve tireden oluşmalı.");
        }

        CheckText("displayName", config.DisplayName, errors);
        if (string.IsNullOrWhiteSpace(config.DisplayName))
        {
            errors.Add("displayName boş olamaz.");
        }

        if (config.Sources.Count == 0)
        {
            errors.Add("sources en az bir kaynak içermeli.");
        }

        foreach (var source in config.Sources)
        {
            CheckText("sources", source, errors);
        }

        if (config.Status == DeviceStatus.WriteVerified && config.Interface != WmiInterface.Wmi1)
        {
            errors.Add("writeVerified yalnızca wmi1 arayüzünde olabilir (yazma yolu WMI1).");
        }
    }

    /// <summary>Free text reaches logs and the UI; a line break or escape sequence could forge a log line.</summary>
    private static void CheckText(string field, string text, List<string> errors)
    {
        if (ConfigText.HasControl(text))
        {
            errors.Add($"{field} kontrol karakteri içeremez.");
        }
    }

    private static void CheckFirmware(DeviceConfig config, List<string> errors)
    {
        var (exact, family) = (config.Firmware.Exact, config.Firmware.Family);
        var location = config.FirmwareLocation;

        if (location.VersionLength < 1 || location.DateLength < 1
            || location.Version + location.VersionLength > RegisterSpace || location.Date + location.DateLength > RegisterSpace)
        {
            errors.Add("firmwareLocation geçersiz: uzunluklar en az 1 olmalı ve 0xFF'i aşmamalı.");
        }

        if (family is not null)
        {
            CheckText("firmware.family", family, errors);
            if (family.Length != FamilyLength)
            {
                errors.Add($"firmware.family \"{ConfigText.Show(family)}\" {FamilyLength} karakter olmalı.");
            }
        }

        foreach (var firmware in exact)
        {
            CheckExactFirmware(firmware, family, config, errors);
        }

        foreach (var duplicate in Duplicates(exact))
        {
            errors.Add($"firmware \"{ConfigText.Show(duplicate)}\" iki kez yazılmış.");
        }

        if (config.Status == DeviceStatus.WriteVerified && exact.Count == 0)
        {
            errors.Add("writeVerified kayıtta tam firmware listesi boş olamaz.");
        }
    }

    private static void CheckExactFirmware(string firmware, string? family, DeviceConfig config, List<string> errors)
    {
        CheckText("firmware", firmware, errors);
        var shown = ConfigText.Show(firmware);

        if (firmware.Length != config.FirmwareLocation.VersionLength)
        {
            errors.Add($"firmware \"{shown}\" {config.FirmwareLocation.VersionLength} karakter olmalı.");
        }
        else if (family is not null && !firmware.StartsWith(family, StringComparison.Ordinal))
        {
            errors.Add($"firmware \"{shown}\" \"{ConfigText.Show(family)}\" ailesinde değil.");
        }

        if (config.Status == DeviceStatus.WriteVerified && !WriteVerifiedFirmware.Contains(firmware))
        {
            errors.Add($"firmware \"{shown}\" kodda yazma için doğrulanmış değil; writeVerified olamaz.");
        }
    }

    private static void CheckLimits(CurveLimits limits, List<string> errors)
    {
        if (limits.MinUpThresholdC < EcWriteRules.MinUpThresholdC || limits.MaxUpThresholdC > EcWriteRules.MaxUpThresholdC
            || limits.MinUpThresholdC > limits.MaxUpThresholdC
            || limits.MaxSpeedPercent is < 0 or > EcWriteRules.MaxSpeedPercent)
        {
            errors.Add($"limits kod tavanını aşıyor: eşik {EcWriteRules.MinUpThresholdC}-{EcWriteRules.MaxUpThresholdC} °C, "
                + $"hız en fazla %{EcWriteRules.MaxSpeedPercent} olmalı.");
        }
    }

    private static void CheckFans(DeviceConfig config, List<string> errors)
    {
        if (config.Fans.Count == 0)
        {
            errors.Add("En az bir fan olmalı.");
        }

        foreach (var id in Duplicates(config.Fans.Select(f => f.Id)))
        {
            errors.Add($"fan id \"{ConfigText.Show(id)}\" iki kez kullanılmış.");
        }

        foreach (var fan in config.Fans)
        {
            CheckFan(fan, config, errors);
        }
    }

    private static void CheckFan(FanConfig fan, DeviceConfig config, List<string> errors)
    {
        var id = ConfigText.Show(fan.Id);
        if (!IdPattern().IsMatch(fan.Id))
        {
            errors.Add($"fan id \"{id}\" küçük harf, rakam ve tireden oluşmalı.");
        }

        CheckBlock(id, "yukarı eşik", fan.UpThresholds, ThresholdCount, errors);
        CheckBlock(id, "hız", fan.Speeds, SpeedCount, errors);

        if (fan.RpmHigh == byte.MaxValue)
        {
            errors.Add($"{id}: RPM düşük baytı 0xFF'i aşıyor.");
        }

        var offsets = fan.FactoryDownOffsets;
        if (offsets is not null && !AreValidOffsets(offsets))
        {
            errors.Add($"{id}: iniş ofsetleri {ThresholdCount} adet, {MinDownOffsetC}-{MaxDownOffsetC} °C olmalı ({string.Join(", ", offsets)}).");
        }

        if (fan.FactoryCurve is { } curve)
        {
            if (offsets is null)
            {
                errors.Add($"{id}: fabrika eğrisi iniş ofsetleri olmadan doğrulanamaz.");
            }
            else if (AreValidOffsets(offsets))
            {
                CheckCurve($"{id} fabrika eğrisi", curve, offsets, config.Limits, errors);
            }
        }

        if (config.Status == DeviceStatus.WriteVerified && (offsets is null || fan.FactoryCurve is null))
        {
            errors.Add($"{id}: writeVerified kayıtta fabrika eğrisi ve iniş ofsetleri zorunlu.");
        }
    }

    private static void CheckBlock(string fanId, string name, RegisterBlock block, int expected, List<string> errors)
    {
        if (block.Count != expected)
        {
            errors.Add($"{fanId}: {name} bloğu {expected} register olmalı ({block.Count} var).");
        }

        if (block.Start + block.Count > RegisterSpace)
        {
            errors.Add($"{fanId}: 0x{block.Start:X2} ile başlayan {name} bloğu 0xFF'i aşıyor.");
        }
    }

    private static bool AreValidOffsets(IReadOnlyList<int> offsets) =>
        offsets.Count == ThresholdCount && offsets.All(o => o is >= MinDownOffsetC and <= MaxDownOffsetC);

    /// <summary>The same whole-curve rules as the gateway, with this record's offsets, plus the record's own limits.</summary>
    private static void CheckCurve(string label, FanCurve curve, IReadOnlyList<int> offsets, CurveLimits limits, List<string> errors)
    {
        errors.AddRange(CurveValidator.Validate(curve, offsets).Select(e => $"{label}: {e}"));

        var outside = curve.Points.Skip(1).Any(p => p.UpThresholdC < limits.MinUpThresholdC || p.UpThresholdC > limits.MaxUpThresholdC)
            || curve.Points.Any(p => p.SpeedPercent > limits.MaxSpeedPercent);
        if (outside)
        {
            errors.Add($"{label}: kaydın limitleri dışında ({limits.MinUpThresholdC}-{limits.MaxUpThresholdC} °C, "
                + $"en fazla %{limits.MaxSpeedPercent}).");
        }
    }

    private static void CheckFeatures(FeatureSet features, List<string> errors)
    {
        if (features.CoolerBoost is { Bit: < 0 or > MaxBit })
        {
            errors.Add($"coolerBoost.bit 0-{MaxBit} olmalı.");
        }

        if (features.ChargeLimit is { } charge
            && (charge.EnableBit != ChargeLimitEnableBit
                || charge.Min < EcWriteRules.MinChargeLimitPercent || charge.Max > EcWriteRules.MaxChargeLimitPercent
                || charge.Min > charge.Max))
        {
            errors.Add($"chargeLimit kod tavanını aşıyor: enableBit {ChargeLimitEnableBit} ve "
                + $"{EcWriteRules.MinChargeLimitPercent} ≤ min ≤ max ≤ {EcWriteRules.MaxChargeLimitPercent} olmalı.");
        }

        if (features.PerformanceMode is { } performance)
        {
            CheckPerformanceModes(performance.Modes, errors);
        }

        if (features.FanMode is { } fanMode && fanMode.Auto == fanMode.Advanced)
        {
            errors.Add("fanMode.auto ve fanMode.advanced farklı olmalı.");
        }
    }

    private static void CheckPerformanceModes(IReadOnlyList<ModeValue> modes, List<string> errors)
    {
        if (modes.Count == 0)
        {
            errors.Add("performanceMode en az bir mod içermeli.");
        }

        foreach (var mode in modes.Where(m => !IdPattern().IsMatch(m.Id)))
        {
            errors.Add($"performanceMode id \"{ConfigText.Show(mode.Id)}\" küçük harf, rakam ve tireden oluşmalı.");
        }

        foreach (var id in Duplicates(modes.Select(m => m.Id)))
        {
            errors.Add($"performanceMode id \"{ConfigText.Show(id)}\" iki kez kullanılmış.");
        }

        foreach (var value in Duplicates(modes.Select(m => m.Value)))
        {
            errors.Add($"performanceMode değeri 0x{value:X2} iki kez kullanılmış.");
        }
    }

    private static void CheckPresets(DeviceConfig config, List<string> errors)
    {
        foreach (var name in Duplicates(config.Presets.Select(p => p.Name)))
        {
            errors.Add($"preset \"{ConfigText.Show(name)}\" iki kez var.");
        }

        var fans = config.Fans.DistinctBy(f => f.Id).ToDictionary(f => f.Id);
        foreach (var preset in config.Presets)
        {
            CheckPreset(preset, fans, config.Limits, errors);
        }
    }

    private static void CheckPreset(PresetConfig preset, Dictionary<string, FanConfig> fans, CurveLimits limits, List<string> errors)
    {
        CheckText("preset adı", preset.Name, errors);
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            errors.Add("preset adı boş olamaz.");
        }

        var name = ConfigText.Show(preset.Name);
        foreach (var unknown in preset.Curves.Keys.Where(k => !fans.ContainsKey(k)))
        {
            errors.Add($"{name}: \"{ConfigText.Show(unknown)}\" diye bir fan yok.");
        }

        foreach (var fan in fans.Values)
        {
            var fanId = ConfigText.Show(fan.Id);
            if (!preset.Curves.TryGetValue(fan.Id, out var curve))
            {
                errors.Add($"{name}: {fanId} fanının eğrisi yok.");
            }
            else if (fan.FactoryDownOffsets is null)
            {
                errors.Add($"{name}: {fanId} fanının iniş ofsetleri yok, preset doğrulanamaz.");
            }
            else if (AreValidOffsets(fan.FactoryDownOffsets))
            {
                CheckCurve($"{name} ({fanId})", curve, fan.FactoryDownOffsets, limits, errors);
            }
        }
    }

    private static IEnumerable<T> Duplicates<T>(IEnumerable<T> values) =>
        values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key);
}
