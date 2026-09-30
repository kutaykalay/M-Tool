using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Settings;

public sealed record SanitizedSettings(AppSettings Settings, IReadOnlyList<string> Warnings);

/// <summary>
/// Checks settings.json values that end up in EC writes. The file is outside input: a hand-edited
/// value must never reach the gateway. Bad fields are dropped (never written), not guessed.
/// </summary>
public static class SettingsSanitizer
{
    public static SanitizedSettings Sanitize(AppSettings settings, ProfileCatalog catalog)
    {
        var warnings = new List<string>();
        var desired = settings.Desired;

        var profile = desired.FanProfile is null ? null : catalog.Find(desired.FanProfile);
        if (profile is null)
        {
            warnings.Add($"Bilinmeyen fan profili ({desired.FanProfile ?? "boş"}); {DesiredState.DefaultProfileName} kullanılıyor.");
        }

        desired = desired with
        {
            FanProfile = profile?.Name ?? DesiredState.DefaultProfileName,
            Performance = Defined(desired.Performance, "performans modu", warnings),
            ChargeLimitPercent = ValidChargeLimit(desired.ChargeLimitPercent, warnings),
            FanMode = Defined(desired.FanMode, "fan modu", warnings),
        };

        return new SanitizedSettings(settings with { Desired = desired }, warnings.AsReadOnly());
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
