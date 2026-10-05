using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Settings;

/// <param name="Catalog">The built-in profiles plus the custom profiles that passed.</param>
/// <param name="DroppedProfiles">Custom profiles set aside; when above zero, keep a copy of the file before saving.</param>
public sealed record SanitizedSettings(
    AppSettings Settings, IReadOnlyList<string> Warnings, ProfileCatalog Catalog, int DroppedProfiles);

/// <summary>
/// Checks settings.json values that end up in EC writes. The file is outside input: a hand-edited
/// value must never reach the gateway. Bad fields are dropped (never written), not guessed; a custom
/// profile is kept exactly as stored or dropped as a whole.
/// </summary>
public static class SettingsSanitizer
{
    /// <param name="catalog">The built-in profiles; the custom profiles that pass are added to them.</param>
    public static SanitizedSettings Sanitize(AppSettings settings, ProfileCatalog catalog)
    {
        var warnings = new List<string>();
        var custom = KeptCustomProfiles(settings.CustomProfiles, catalog, warnings);
        var dropped = settings.CustomProfiles.Count - custom.Count;
        var full = catalog.WithCustom(custom);
        var desired = settings.Desired;

        var profile = desired.FanProfile is null ? null : full.Find(desired.FanProfile);
        if (profile is null)
        {
            var name = string.IsNullOrWhiteSpace(desired.FanProfile) ? "boş" : ProfileNameRules.Printable(desired.FanProfile);
            warnings.Add($"Bilinmeyen fan profili ({name}); {DesiredState.DefaultProfileName} kullanılıyor.");
        }

        desired = desired with
        {
            FanProfile = profile?.Name ?? DesiredState.DefaultProfileName,
            Performance = Defined(desired.Performance, "performans modu", warnings),
            ChargeLimitPercent = ValidChargeLimit(desired.ChargeLimitPercent, warnings),
            FanMode = Defined(desired.FanMode, "fan modu", warnings),
        };

        return new SanitizedSettings(
            settings with { Desired = desired, CustomProfiles = custom }, warnings.AsReadOnly(), full, dropped);
    }

    /// <summary>Dropped profiles named one by one; the rest are counted in a single warning.</summary>
    public const int MaxProfileWarnings = 10;

    /// <summary>
    /// In file order: structure first (records compare their parts), then the count, so a huge list costs
    /// no name or curve checks, then name and curves.
    /// </summary>
    private static IReadOnlyList<FanProfile> KeptCustomProfiles(
        IReadOnlyList<FanProfile> profiles, ProfileCatalog catalog, List<string> warnings)
    {
        var kept = new List<FanProfile>();
        var dropped = 0;
        foreach (var profile in profiles)
        {
            var problem = MissingPart(profile)
                ?? (kept.Count >= ProfileCatalog.MaxCustomProfiles ? $"en fazla {ProfileCatalog.MaxCustomProfiles} özel profil olabilir" : null)
                ?? NameProblem(profile.Name, catalog.WithCustom(kept))
                ?? CurveProblem(profile.Curves);
            if (problem is null)
            {
                kept.Add(profile);
            }
            else if (++dropped <= MaxProfileWarnings)
            {
                warnings.Add($"Özel profil \"{ProfileNameRules.Printable(profile?.Name)}\" kenara ayrıldı: {problem.TrimEnd('.')}.");
            }
        }

        if (dropped > MaxProfileWarnings)
        {
            warnings.Add($"{dropped - MaxProfileWarnings} özel profil daha kenara ayrıldı.");
        }

        return kept.AsReadOnly();
    }

    private static string? MissingPart(FanProfile? profile) =>
        profile?.Name is null || profile.Curves?.Cpu?.Points is null || profile.Curves.Gpu?.Points is null
        || profile.Curves.Cpu.Points.Any(p => p is null) || profile.Curves.Gpu.Points.Any(p => p is null)
            ? "eksik alan."
            : null;

    private static string? NameProblem(string name, ProfileCatalog catalog)
    {
        if (ProfileNameRules.Validate(name, catalog) is { } error)
        {
            return error;
        }

        return name == ProfileNameRules.Normalize(name) ? null : "ad başında ya da sonunda boşlukla ya da ayrışık harfle kaydedilmiş.";
    }

    /// <summary>The same check the gateway makes; a rule tightened in a later version also lands here.</summary>
    private static string? CurveProblem(FanCurves curves)
    {
        var cpu = CurveValidator.Validate(curves.Cpu, FactoryDefaults.CpuDownOffsets);
        var gpu = CurveValidator.Validate(curves.Gpu, FactoryDefaults.GpuDownOffsets);
        return cpu.Count > 0 ? $"CPU eğrisi fan kurallarına uymuyor ({cpu[0]})"
            : gpu.Count > 0 ? $"GPU eğrisi fan kurallarına uymuyor ({gpu[0]})"
            : null;
    }

    private static T? Defined<T>(T? value, string name, List<string> warnings)
        where T : struct, Enum
    {
        if (value is not { } v || Enum.IsDefined(v))
        {
            return value;
        }

        warnings.Add($"Geçersiz {name} ({v}); yok sayıldı.");
        return null;
    }

    private static int? ValidChargeLimit(int? percent, List<string> warnings)
    {
        if (percent is null or (>= EcWriteRules.MinChargeLimitPercent and <= EcWriteRules.MaxChargeLimitPercent))
        {
            return percent;
        }

        warnings.Add(
            $"Geçersiz şarj limiti (%{percent}); %{EcWriteRules.MinChargeLimitPercent}-{EcWriteRules.MaxChargeLimitPercent} olmalı, yok sayıldı.");
        return null;
    }
}
