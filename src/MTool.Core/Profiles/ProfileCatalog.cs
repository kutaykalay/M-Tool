using MTool.Core.Device;

namespace MTool.Core.Profiles;

/// <summary>The fan profiles the user can pick.</summary>
public sealed class ProfileCatalog(IReadOnlyList<FanProfile> profiles)
{
    public static ProfileCatalog BuiltIn { get; } = new([FactoryDefaults.Profile, Presets.Cool, Presets.Silent]);

    /// <summary>The most custom profiles the user can keep, so the window and tray menu stay usable.</summary>
    public const int MaxCustomProfiles = 10;

    public IReadOnlyList<FanProfile> Profiles { get; } = profiles;

    public FanProfile? Find(string name) =>
        Profiles.FirstOrDefault(p => SameName(p.Name, name));

    /// <summary>Whether <paramref name="name"/> is one of the profiles that ship with M-Tool, ignoring case.</summary>
    public static bool IsBuiltIn(string name) => BuiltIn.Find(name) is not null;

    /// <summary>A new catalog: the built-in profiles, then <paramref name="custom"/> in its order.</summary>
    /// <remarks>
    /// Previous custom profiles are replaced. Names are not checked here; the caller rejects empty, taken
    /// and duplicate names first (<see cref="ProfileNameRules"/>), otherwise <see cref="Find"/> returns the first.
    /// </remarks>
    public ProfileCatalog WithCustom(IReadOnlyList<FanProfile> custom) => new([.. BuiltIn.Profiles, .. custom]);

    /// <summary>The profile whose tables equal <paramref name="curves"/>, or null for a table M-Tool did not write.</summary>
    /// <param name="preferredName">
    /// Wins when several profiles have the same tables, e.g. an unchanged copy of Cool. Usually the profile
    /// the user asked for; otherwise the first match is returned.
    /// </param>
    public FanProfile? Match(FanCurves curves, string? preferredName = null)
    {
        var matches = Profiles.Where(p => p.Curves == curves).ToList();
        return matches.FirstOrDefault(p => SameName(p.Name, preferredName)) ?? matches.FirstOrDefault();
    }

    private static bool SameName(string name, string? other) =>
        string.Equals(name, other, StringComparison.OrdinalIgnoreCase);
}
