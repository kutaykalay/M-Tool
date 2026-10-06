using MTool.Core.Profiles;
using MTool.Core.Settings;

namespace MTool.Core.Power;

/// <summary>
/// Separate choices on AC and on battery, as new settings; the input is never changed. While switching
/// is on, the fan profile and performance mode of the desired state equal the pair of the current
/// source: a switch brings the pair back (<see cref="Align"/>), a choice by hand becomes the pair
/// (<see cref="Record"/>). The charge limit and fan mode are never part of a pair. Nothing here
/// touches the EC or the file.
/// </summary>
public static class PowerSwitchRules
{
    /// <summary>
    /// After a switch to <paramref name="source"/>. Without a pair yet, what is set now stays and is
    /// remembered as that source's pair.
    /// </summary>
    public static AppSettings Align(AppSettings settings, PowerSource source)
    {
        if (!settings.PowerSwitch.Enabled)
        {
            return settings;
        }

        if (PairFor(settings.PowerSwitch, source) is not { } pair)
        {
            return Record(settings, source);
        }

        return settings with
        {
            Desired = settings.Desired with
            {
                FanProfile = pair.FanProfile,
                Performance = pair.Performance ?? settings.Desired.Performance,
            },
        };
    }

    /// <summary>After the user changed the fan profile or performance mode while on <paramref name="source"/>.</summary>
    public static AppSettings Record(AppSettings settings, PowerSource source) =>
        settings.PowerSwitch.Enabled
            ? settings with { PowerSwitch = WithPair(settings.PowerSwitch, source, PairOf(settings.Desired)) }
            : settings;

    /// <summary>
    /// Turning switching on remembers what is set now for the current source, so it writes nothing.
    /// Turning it off keeps both pairs for the next time.
    /// </summary>
    public static AppSettings Enable(AppSettings settings, bool enabled, PowerSource? source)
    {
        var flagged = settings with { PowerSwitch = settings.PowerSwitch with { Enabled = enabled } };
        return enabled && source is { } current ? Record(flagged, current) : flagged;
    }

    public static PowerProfilePair? PairFor(PowerSwitchSettings powerSwitch, PowerSource source) =>
        source == PowerSource.Ac ? powerSwitch.Ac : powerSwitch.Battery;

    private static PowerSwitchSettings WithPair(PowerSwitchSettings powerSwitch, PowerSource source, PowerProfilePair? pair) =>
        source == PowerSource.Ac ? powerSwitch with { Ac = pair } : powerSwitch with { Battery = pair };

    private static PowerProfilePair PairOf(DesiredState desired) => new(desired.FanProfile, desired.Performance);
}
