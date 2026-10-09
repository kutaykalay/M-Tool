using System.Globalization;
using MTool.Core.Resources;

namespace MTool.Core.Localization;

/// <summary>
/// Texts of <see cref="CoreStrings"/> in a chosen language. Code that builds one sentence out of
/// another (a warning around a reason) passes the language down, so both parts are in the same one.
/// </summary>
internal static class CoreText
{
    /// <param name="key">Written as <c>nameof(CoreStrings.X)</c>, so a wrong key does not compile.</param>
    /// <param name="culture">Numbers are written in it too. <see cref="CultureInfo.InvariantCulture"/> gives the neutral English text.</param>
    public static string Get(string key, CultureInfo culture, params object?[] args) =>
        string.Format(
            culture,
            CoreStrings.ResourceManager.GetString(key, culture) ?? throw new InvalidOperationException($"Missing string resource {key}."),
            args);

    /// <summary>
    /// The same text for the user (in the current UI language) and for the log (always English, whatever
    /// the user reads). <paramref name="make"/> runs twice, so it must have no side effects.
    /// </summary>
    public static (string Text, string Log) ForUserAndLog(Func<CultureInfo, string> make) =>
        (make(CultureInfo.CurrentUICulture), make(CultureInfo.InvariantCulture));
}
