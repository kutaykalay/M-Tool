using MTool.Core.Power;
using MTool.Core.Settings;

namespace MTool.Core.Profiles;

/// <summary>Either the new settings or a human-readable (Turkish) reason why nothing changed.</summary>
public sealed record LibraryResult(AppSettings? Updated, string? Error)
{
    public static LibraryResult Ok(AppSettings updated) => new(updated, null);

    public static LibraryResult Fail(string error) => new(null, error);
}

/// <summary>
/// Changes to the user's custom fan profiles, as new settings; the input is never changed. Built-in
/// profiles cannot be changed. Nothing here touches the EC or the file: the caller saves the result.
/// </summary>
public static class ProfileLibrary
{
    public static LibraryResult Add(AppSettings settings, string name, FanCurves curves)
    {
        if (settings.CustomProfiles.Count >= ProfileCatalog.MaxCustomProfiles)
        {
            return LibraryResult.Fail($"En fazla {ProfileCatalog.MaxCustomProfiles} özel profil olabilir; önce birini silin.");
        }

        var problem = ProfileNameRules.Validate(name, CatalogOf(settings)) ?? CurveProblem(curves);
        return problem is not null
            ? LibraryResult.Fail(problem)
            : LibraryResult.Ok(settings with
            {
                CustomProfiles = [.. settings.CustomProfiles, new FanProfile(ProfileNameRules.Normalize(name), Snapshot(curves))],
            });
    }

    /// <summary>The profile keeps its place and curves; the desired state follows it when it was the desired one.</summary>
    public static LibraryResult Rename(AppSettings settings, string oldName, string newName)
    {
        if (CustomIndex(settings, oldName) is not { } index)
        {
            return LibraryResult.Fail(NotCustom(oldName));
        }

        var current = settings.CustomProfiles[index];
        if (ProfileNameRules.Validate(newName, CatalogOf(settings), except: current.Name) is { } problem)
        {
            return LibraryResult.Fail(problem);
        }

        var renamed = current with { Name = ProfileNameRules.Normalize(newName) };
        var desired = IsReferenced(settings, current.Name)
            ? settings.Desired with { FanProfile = renamed.Name }
            : settings.Desired;
        return LibraryResult.Ok(settings with
        {
            CustomProfiles = Replace(settings, index, renamed),
            Desired = desired,
            PowerSwitch = PairsAfter(settings.PowerSwitch, current.Name, renamed.Name),
        });
    }

    /// <summary>
    /// The desired profile cannot be deleted: that would need a fan table write as a side effect. A power
    /// pair naming it is emptied instead; switching to that source then keeps what is set.
    /// </summary>
    public static LibraryResult Delete(AppSettings settings, string name)
    {
        if (CustomIndex(settings, name) is not { } index)
        {
            return LibraryResult.Fail(NotCustom(name));
        }

        if (IsReferenced(settings, settings.CustomProfiles[index].Name))
        {
            return LibraryResult.Fail("Bu profil şu an seçili; silmeden önce başka bir profil seçin.");
        }

        return LibraryResult.Ok(settings with
        {
            CustomProfiles = [.. settings.CustomProfiles.Where((_, i) => i != index)],
            PowerSwitch = PairsAfter(settings.PowerSwitch, settings.CustomProfiles[index].Name, newName: null),
        });
    }

    /// <summary>Saving does not write the EC; when this is the desired profile the caller shows the drift.</summary>
    public static LibraryResult SaveCurves(AppSettings settings, string name, FanCurves curves)
    {
        if (CustomIndex(settings, name) is not { } index)
        {
            return LibraryResult.Fail(NotCustom(name));
        }

        return CurveProblem(curves) is { } problem
            ? LibraryResult.Fail(problem)
            : LibraryResult.Ok(settings with
            {
                CustomProfiles = Replace(settings, index, settings.CustomProfiles[index] with { Curves = Snapshot(curves) }),
            });
    }

    /// <summary>The desired state names the profile; power pairs are handled by <see cref="PairsAfter"/>.</summary>
    private static bool IsReferenced(AppSettings settings, string name) =>
        string.Equals(settings.Desired.FanProfile, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pairs naming <paramref name="name"/> take <paramref name="newName"/>, or are emptied when it is null.</summary>
    private static PowerSwitchSettings PairsAfter(PowerSwitchSettings powerSwitch, string name, string? newName) =>
        powerSwitch with { Ac = PairAfter(powerSwitch.Ac, name, newName), Battery = PairAfter(powerSwitch.Battery, name, newName) };

    private static PowerProfilePair? PairAfter(PowerProfilePair? pair, string name, string? newName)
    {
        if (pair is null || !string.Equals(pair.FanProfile, name, StringComparison.OrdinalIgnoreCase))
        {
            return pair;
        }

        return newName is null ? null : pair with { FanProfile = newName };
    }

    private static ProfileCatalog CatalogOf(AppSettings settings) => ProfileCatalog.BuiltIn.WithCustom(settings.CustomProfiles);

    private static int? CustomIndex(AppSettings settings, string name)
    {
        for (var i = 0; i < settings.CustomProfiles.Count; i++)
        {
            if (string.Equals(settings.CustomProfiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    private static string NotCustom(string name) => ProfileCatalog.IsBuiltIn(name)
        ? $"\"{name}\" hazır bir profil, değiştirilemez; kopyalayıp kopyasını düzenleyin."
        : $"\"{ProfileNameRules.Printable(name)}\" adında bir özel profil bulunamadı.";

    private static IReadOnlyList<FanProfile> Replace(AppSettings settings, int index, FanProfile profile) =>
        [.. settings.CustomProfiles.Select((p, i) => i == index ? profile : p)];

    private static string? CurveProblem(FanCurves curves) =>
        !CurveValidator.IsComplete(curves) ? "Fan eğrisi eksik."
        : CurveValidator.ValidateWithFactoryOffsets(curves) is [var first, ..] ? first
        : null;

    /// <summary>The caller's point lists may change later; the stored profile must not change with them.</summary>
    private static FanCurves Snapshot(FanCurves curves) =>
        new(new FanCurve(Array.AsReadOnly(curves.Cpu.Points.ToArray())), new FanCurve(Array.AsReadOnly(curves.Gpu.Points.ToArray())));
}
