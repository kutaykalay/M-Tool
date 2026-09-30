using MTool.Core.Device;

namespace MTool.Core.Profiles;

/// <summary>The fan profiles the user can pick. Stage 5 adds the user's own profiles.</summary>
public sealed class ProfileCatalog(IReadOnlyList<FanProfile> profiles)
{
    public static ProfileCatalog BuiltIn { get; } = new([FactoryDefaults.Profile, Presets.Cool, Presets.Silent]);

    public IReadOnlyList<FanProfile> Profiles { get; } = profiles;

    public FanProfile? Find(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The profile whose tables equal <paramref name="curves"/>, or null for a table M-Tool did not write.</summary>
    public FanProfile? Match(FanCurves curves) => Profiles.FirstOrDefault(p => p.Curves == curves);
}
