namespace MTool.Core.Localization;

/// <summary>
/// The user interface languages M-Tool ships. A language needs a resx pair (CoreStrings and Strings),
/// a line here and an entry in the App's SatelliteResourceLanguages.
/// </summary>
/// <remarks>
/// Names are canonical .NET culture names: a neutral language ("de") unless a region or script
/// matters ("pt-BR", "zh-Hans"). settings.json stores them in this exact spelling.
/// </remarks>
public static class SupportedLanguages
{
    /// <summary>English: the neutral resources, and the fallback for every language M-Tool lacks.</summary>
    public const string Neutral = "en";

    public static IReadOnlyList<string> All { get; } = [Neutral, "tr"];
}
