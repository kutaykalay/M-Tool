using MTool.Core.Device;

namespace MTool.Core.Power;

/// <summary>Where the laptop draws power from. A source Windows cannot tell is null, never a member.</summary>
public enum PowerSource
{
    Ac,
    Battery,
}

/// <summary>
/// What a power source brings back: the fan profile and the performance mode, the WMI parts of
/// <see cref="Profiles.DesiredState"/>. A null performance is left as it is, like in the desired state.
/// Outside input: <see cref="Settings.SettingsSanitizer"/> checks the profile name.
/// </summary>
public sealed record PowerProfilePair(string FanProfile, PerformanceMode? Performance = null);

/// <param name="Enabled">Off by default; turning it on writes nothing.</param>
/// <param name="Ac">Null until the first choice on AC is remembered.</param>
/// <param name="Battery">Null until the first choice on battery is remembered.</param>
public sealed record PowerSwitchSettings(bool Enabled = false, PowerProfilePair? Ac = null, PowerProfilePair? Battery = null)
{
    public static PowerSwitchSettings Default { get; } = new();
}
