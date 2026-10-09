using System.Globalization;
using MTool.Core.Resources;

namespace MTool.Core.Localization;

/// <summary>
/// One message in two languages: <see cref="Text"/> for the user (the current UI language) and <see cref="Log"/> for the
/// log (always English, so a log reads the same whatever language the user runs). Built by <see cref="Say"/>, which
/// fills a <see cref="CoreStrings"/> text in both languages. Must be built on a thread that has the user's UI language.
/// </summary>
internal sealed record UserAndLogText(string Text, string Log)
{
    /// <summary>Test hook: called with the key before every <see cref="Say"/> lookup; a test makes it throw to fail the lookup. Flows with the async context.</summary>
    internal static readonly AsyncLocal<Action<string>?> LookupFault = new();

    /// <summary>A text that does not depend on the language (a register list, a message from the system).</summary>
    public static UserAndLogText Plain(string text) => new(text, text);

    /// <summary>
    /// Never throws: the write path builds its messages around EC writes, where a failed resource lookup must not
    /// skip the lock, the recovery or an outcome. A lookup or format that fails gives the key and its arguments instead.
    /// </summary>
    /// <param name="key">Written as <c>nameof(CoreStrings.X)</c>.</param>
    /// <param name="args">Arguments of the text; one that is a <see cref="UserAndLogText"/> is used in the matching language.</param>
    public static UserAndLogText Say(string key, params object?[] args)
    {
        try
        {
            LookupFault.Value?.Invoke(key);
            return new(
                CoreText.Get(key, CultureInfo.CurrentUICulture, [.. args.Select(a => a is UserAndLogText t ? t.Text : a)]),
                CoreText.Get(key, CultureInfo.InvariantCulture, [.. args.Select(a => a is UserAndLogText t ? t.Log : a)]));
        }
        catch (Exception)
        {
            return new(Fallback(key, args, a => a.Text), Fallback(key, args, a => a.Log));
        }
    }

    /// <summary>The parts in each language, joined by <paramref name="separator"/>.</summary>
    public static UserAndLogText Join(char separator, IEnumerable<UserAndLogText> parts)
    {
        var all = parts.ToArray();
        return new(string.Join(separator, all.Select(p => p.Text)), string.Join(separator, all.Select(p => p.Log)));
    }

    private static string Fallback(string key, object?[] args, Func<UserAndLogText, string> pick)
    {
        try
        {
            return args.Length == 0 ? key : $"{key}: {string.Join("; ", args.Select(a => a is UserAndLogText t ? pick(t) : a?.ToString()))}";
        }
        catch (Exception)
        {
            return key;
        }
    }
}
