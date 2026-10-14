using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MTool.Core.Diagnostics;

/// <summary>
/// Keeps the user and computer names out of text that leaves the laptop (the report, the issue form)
/// and tells which characters are not shown. One place, so every way out hides the same words.
/// <see cref="Mask"/> works on the text as written; <see cref="HoldsPrivateName"/> also finds look-alike
/// forms (NFKC), so a value that must not carry a name at all is checked with it.
/// </summary>
public static class PrivateText
{
    public const string Hidden = "<hidden>";

    /// <summary>Shorter words would cut into ordinary text.</summary>
    private const int MinWordLength = 2;

    /// <summary>Whole words only, ignoring case: a short name must not cut into other words.</summary>
    internal static string Mask(string text, IReadOnlyCollection<string> privateWords)
    {
        foreach (var word in Usable(privateWords).OrderByDescending(w => w.Length))
        {
            text = Regex.Replace(text, $@"(?<![\w-]){Regex.Escape(word)}(?![\w-])", Hidden, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }

    /// <summary>
    /// Whether the value holds a private name anywhere. Both sides are compared in their NFKC form, so a
    /// name written with full-width or other look-alike letters is still found. A value that is not valid
    /// Unicode (a lone surrogate) cannot be compared and counts as private.
    /// </summary>
    public static bool HoldsPrivateName(string value, IReadOnlyCollection<string> privateWords)
    {
        if (!TryFold(value, out var folded))
        {
            return true;
        }

        return Usable(privateWords).Any(word =>
            folded.Contains(TryFold(word, out var foldedWord) ? foldedWord : word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Characters that are not shown or that change how the text is shown: format characters (the
    /// right-to-left override, zero-width characters), private use, line and paragraph
    /// separators, and control characters. A control character that is white space (tab, line break)
    /// is not unprintable: it counts as a space.
    /// </summary>
    public static bool IsUnprintable(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
        UnicodeCategory.Control => !Rune.IsWhiteSpace(rune),
        _ => false,
    };

    private static IEnumerable<string> Usable(IReadOnlyCollection<string> privateWords) =>
        privateWords.Select(w => w.Trim()).Where(w => w.Length >= MinWordLength);

    /// <returns>False when the text is not valid Unicode (a lone surrogate) and cannot be normalized.</returns>
    private static bool TryFold(string text, out string folded)
    {
        try
        {
            folded = text.Normalize(NormalizationForm.FormKC);
            return true;
        }
        catch (ArgumentException)
        {
            folded = text;
            return false;
        }
    }
}
