using System.Globalization;

namespace MTool.Core.Localization;

/// <summary>Picks the user interface language. Pure: the Windows language is passed in, nothing global is read.</summary>
public static class UiLanguage
{
    /// <summary>
    /// The language the choice in settings.json names, else the Windows language walked up from region to
    /// language ("de-AT", "de"; "zh-CN", "zh-Hans", "zh"), else English. A choice that is not supported
    /// (a hand-edited or newer file) is ignored.
    /// </summary>
    /// <returns>A culture of the <paramref name="supported"/> list, spelled as the list spells it.</returns>
    public static CultureInfo Resolve(string? setting, CultureInfo windowsUi, IReadOnlyList<string> supported)
    {
        var name = CanonicalName(setting, supported) ?? FromWindows(windowsUi, supported);
        return CultureInfo.GetCultureInfo(name);
    }

    /// <summary>The spelling in <paramref name="supported"/> of <paramref name="name"/> (ignoring case), or null.</summary>
    public static string? CanonicalName(string? name, IReadOnlyList<string> supported) =>
        name is null ? null : supported.FirstOrDefault(language => string.Equals(language, name, StringComparison.OrdinalIgnoreCase));

    private static string FromWindows(CultureInfo windowsUi, IReadOnlyList<string> supported)
    {
        for (var culture = windowsUi; culture.Name.Length > 0; culture = culture.Parent)
        {
            if (CanonicalName(culture.Name, supported) is { } found)
            {
                return found;
            }
        }

        return SupportedLanguages.Neutral;
    }
}
