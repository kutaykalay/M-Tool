using System.Globalization;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Resources;

namespace MTool.Core.Settings;

/// <param name="Warnings">For the user, in the UI language.</param>
/// <param name="LogWarnings">The same warnings for the log, always in English; same count and order as <paramref name="Warnings"/>.</param>
/// <param name="Catalog">The built-in profiles plus the custom profiles that passed.</param>
/// <param name="DroppedProfiles">Custom profiles set aside; when above zero, keep a copy of the file before saving.</param>
public sealed record SanitizedSettings(
    AppSettings Settings, IReadOnlyList<string> Warnings, IReadOnlyList<string> LogWarnings, ProfileCatalog Catalog, int DroppedProfiles);

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
        var warnings = new WarningList();
        var custom = KeptCustomProfiles(settings.CustomProfiles, catalog, warnings);
        var dropped = settings.CustomProfiles.Count - custom.Count;
        var full = catalog.WithCustom(custom);
        var desired = settings.Desired;

        var profile = desired.FanProfile is null ? null : full.Find(desired.FanProfile);
        if (profile is null)
        {
            var named = desired.FanProfile;
            warnings.Add(c => CoreText.Get(nameof(CoreStrings.Settings_UnknownProfile), c, PrintableOrEmpty(named, c), DesiredState.DefaultProfileName));
        }

        desired = desired with
        {
            FanProfile = profile?.Name ?? DesiredState.DefaultProfileName,
            Performance = Defined(desired.Performance, nameof(CoreStrings.Settings_InvalidPerformanceMode), warnings),
            ChargeLimitPercent = ValidChargeLimit(desired.ChargeLimitPercent, warnings),
            FanMode = Defined(desired.FanMode, nameof(CoreStrings.Settings_InvalidFanMode), warnings),
        };

        var powerSwitch = settings.PowerSwitch with
        {
            Ac = KeptPair(settings.PowerSwitch.Ac, PowerSource.Ac, full, warnings),
            Battery = KeptPair(settings.PowerSwitch.Battery, PowerSource.Battery, full, warnings),
        };

        return new SanitizedSettings(
            settings with { Desired = desired, CustomProfiles = custom, PowerSwitch = powerSwitch, Language = ValidLanguage(settings.Language, warnings) },
            warnings.Text.AsReadOnly(), warnings.Log.AsReadOnly(), full, dropped);
    }

    /// <summary>
    /// Only a name from <see cref="SupportedLanguages"/> passes, so a hand-edited value is never used as a
    /// culture or a path. What the file said is echoed made printable and cut short.
    /// </summary>
    private static string? ValidLanguage(string? language, WarningList warnings)
    {
        if (language is null)
        {
            return null;
        }

        if (UiLanguage.CanonicalName(language, SupportedLanguages.All) is { } canonical)
        {
            return canonical;
        }

        warnings.Add(c => CoreText.Get(nameof(CoreStrings.Settings_UnknownLanguage), c, ProfileNameRules.Printable(language, c)));
        return null;
    }

    /// <summary>
    /// A pair whose profile is gone is dropped, not set to Default: switching would then write a table
    /// the user never chose there. Without a pair, the next switch to that source keeps what is set.
    /// </summary>
    private static PowerProfilePair? KeptPair(PowerProfilePair? pair, PowerSource source, ProfileCatalog catalog, WarningList warnings)
    {
        if (pair is null)
        {
            return null;
        }

        if ((pair.FanProfile is null ? null : catalog.Find(pair.FanProfile)) is not { } profile)
        {
            var named = pair.FanProfile;
            var key = source == PowerSource.Ac ? nameof(CoreStrings.Settings_PairMissingAc) : nameof(CoreStrings.Settings_PairMissingBattery);
            warnings.Add(c => CoreText.Get(key, c, PrintableOrEmpty(named, c)));
            return null;
        }

        return new PowerProfilePair(profile.Name, Defined(pair.Performance, nameof(CoreStrings.Settings_InvalidPerformanceMode), warnings));
    }

    /// <summary>Dropped profiles named one by one; the rest are counted in a single warning.</summary>
    public const int MaxProfileWarnings = 10;

    /// <summary>
    /// In file order: structure first (records compare their parts), then the count, so a huge list costs
    /// no name or curve checks, then name and curves.
    /// </summary>
    private static IReadOnlyList<FanProfile> KeptCustomProfiles(
        IReadOnlyList<FanProfile> profiles, ProfileCatalog catalog, WarningList warnings)
    {
        var kept = new List<FanProfile>();
        var dropped = 0;
        foreach (var profile in profiles)
        {
            var problem = MissingPart(profile)
                ?? TooManyProblem(kept.Count)
                ?? NameProblem(profile.Name, catalog.WithCustom(kept))
                ?? CurveProblem(profile.Curves);
            if (problem is null)
            {
                kept.Add(profile);
            }
            else if (++dropped <= MaxProfileWarnings)
            {
                warnings.Add(c => CoreText.Get(
                    nameof(CoreStrings.Settings_ProfileDropped), c, ProfileNameRules.Printable(profile?.Name, c), problem(c).TrimEnd('.')));
            }
        }

        if (dropped > MaxProfileWarnings)
        {
            var more = dropped - MaxProfileWarnings;
            warnings.Add(c => CoreText.Get(nameof(CoreStrings.Settings_ProfilesDroppedMore), c, more));
        }

        return kept.AsReadOnly();
    }

    /// <summary>
    /// The problems are texts made in a language of the caller's choosing; whether there is one does not
    /// depend on the language.
    /// </summary>
    private static Func<CultureInfo, string>? MissingPart(FanProfile? profile) =>
        profile?.Name is null || !CurveValidator.IsComplete(profile.Curves)
            ? c => CoreText.Get(nameof(CoreStrings.Settings_DropMissingPart), c)
            : null;

    private static Func<CultureInfo, string>? TooManyProblem(int keptCount) =>
        keptCount >= ProfileCatalog.MaxCustomProfiles
            ? c => CoreText.Get(nameof(CoreStrings.Settings_DropTooMany), c, ProfileCatalog.MaxCustomProfiles)
            : null;

    private static Func<CultureInfo, string>? NameProblem(string name, ProfileCatalog catalog)
    {
        if (ProfileNameRules.Validate(name, catalog) is not null)
        {
            return c => ProfileNameRules.Validate(name, catalog, culture: c)!;
        }

        return name == ProfileNameRules.Normalize(name) ? null : c => CoreText.Get(nameof(CoreStrings.Settings_DropNameNotNormal), c);
    }

    /// <summary>The same check the gateway makes; a rule tightened in a later version also lands here.</summary>
    private static Func<CultureInfo, string>? CurveProblem(FanCurves curves) =>
        CurveValidator.ValidateWithFactoryOffsets(curves).Count > 0
            ? c => CoreText.Get(nameof(CoreStrings.Settings_DropCurve), c, CurveValidator.ValidateWithFactoryOffsets(curves, c)[0].TrimEnd('.'))
            : null;

    private static string PrintableOrEmpty(string? name, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(name) ? CoreText.Get(nameof(CoreStrings.Settings_EmptyValue), culture) : ProfileNameRules.Printable(name, culture);

    /// <param name="messageKey">The warning for a value that is not defined; {0} is the value.</param>
    private static T? Defined<T>(T? value, string messageKey, WarningList warnings)
        where T : struct, Enum
    {
        if (value is not { } v || Enum.IsDefined(v))
        {
            return value;
        }

        warnings.Add(c => CoreText.Get(messageKey, c, v));
        return null;
    }

    private static int? ValidChargeLimit(int? percent, WarningList warnings)
    {
        if (percent is null or (>= EcWriteRules.MinChargeLimitPercent and <= EcWriteRules.MaxChargeLimitPercent))
        {
            return percent;
        }

        warnings.Add(c => CoreText.Get(
            nameof(CoreStrings.Settings_InvalidChargeLimit), c, percent, EcWriteRules.MinChargeLimitPercent, EcWriteRules.MaxChargeLimitPercent));
        return null;
    }

    /// <summary>
    /// Each warning is made twice: once for the user (UI language) and once for the log (English). What the
    /// log says must not depend on the language the user reads.
    /// </summary>
    private sealed class WarningList
    {
        public List<string> Text { get; } = [];

        public List<string> Log { get; } = [];

        public void Add(Func<CultureInfo, string> make)
        {
            var (text, log) = CoreText.ForUserAndLog(make);
            Text.Add(text);
            Log.Add(log);
        }
    }
}
