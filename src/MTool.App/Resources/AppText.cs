using System.Globalization;

namespace MTool.App.Resources;

/// <summary>
/// One <see cref="Strings"/> text in two languages: <c>Text</c> for the user (the current UI language) and
/// <c>Log</c> for the log (always English), as <c>UserAndLogText</c> does for the core's texts.
/// </summary>
internal static class AppText
{
    /// <summary>
    /// Never throws: it builds the messages of hardware errors, which must stay those errors. A lookup or
    /// format that fails gives the key and its arguments instead.
    /// </summary>
    /// <param name="key">Written as <c>nameof(Strings.X)</c>.</param>
    public static (string Text, string Log) Say(string key, params object?[] args)
    {
        try
        {
            return (Format(key, CultureInfo.CurrentUICulture, args), Format(key, CultureInfo.InvariantCulture, args));
        }
        catch (Exception)
        {
            var fallback = args.Length == 0 ? key : $"{key}: {string.Join("; ", args)}";
            return (fallback, fallback);
        }
    }

    private static string Format(string key, CultureInfo culture, object?[] args) =>
        string.Format(
            culture,
            Strings.ResourceManager.GetString(key, culture) ?? throw new InvalidOperationException($"Missing string resource {key}."),
            args);
}
