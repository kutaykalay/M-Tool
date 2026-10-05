using System.Globalization;
using System.Text;

namespace MTool.Core.Profiles;

/// <summary>Rules for the names of custom fan profiles. A profile's name is its only identity.</summary>
public static class ProfileNameRules
{
    /// <summary>Longest allowed name, in UTF-16 characters, after trimming.</summary>
    public const int MaxLength = 24;

    private const string CopySuffix = " kopya";

    /// <summary>
    /// The name as it is stored: leading and trailing whitespace removed, composed to Unicode form C so a
    /// decomposed "Ç" (C + combining cedilla) is the same name as the single character.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> has an unpaired surrogate; call <see cref="Validate"/> first.</exception>
    public static string Normalize(string name) => name.Trim().Normalize(NormalizationForm.FormC);

    /// <summary>Returns a human-readable (Turkish) problem, or null when <paramref name="name"/> can be used.</summary>
    /// <param name="except">
    /// The custom profile being renamed. Its own name does not count as taken, so only its letter case can
    /// change. It is matched by name, so callers must refuse to rename a built-in profile before calling.
    /// </param>
    /// <remarks>
    /// Names are compared with <see cref="StringComparison.OrdinalIgnoreCase"/>, the same as
    /// <see cref="ProfileCatalog.Find"/>, so "SESSİZ" and "Sessiz" are different names.
    /// </remarks>
    public static string? Validate(string? name, ProfileCatalog catalog, string? except = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Profil adı boş olamaz.";
        }

        // Checked before trimming: a name is written to the log, so no line breaks or text-direction tricks.
        if (name.Any(IsForbidden) || HasUnpairedSurrogate(name))
        {
            return "Profil adında kontrol ya da geçersiz karakter olamaz.";
        }

        var trimmed = Normalize(name);
        if (trimmed.Length > MaxLength)
        {
            return $"Profil adı en fazla {MaxLength} karakter olabilir ({trimmed.Length} var).";
        }

        return IsTaken(trimmed, catalog, except) ? $"\"{trimmed}\" adında bir profil zaten var." : null;
    }

    /// <summary>
    /// A name from outside input made safe for a warning or log line: forbidden characters become "?"
    /// and long names are cut. Never use it to repair a name that is stored.
    /// </summary>
    public static string Printable(string? name)
    {
        if (name is null)
        {
            return "(adsız)";
        }

        var text = new StringBuilder(Math.Min(name.Length, MaxLength + 1));
        var i = 0;
        for (; i < name.Length && text.Length < MaxLength; i++)
        {
            if (char.IsSurrogatePair(name, i))
            {
                text.Append(name, i++, 2);
            }
            else
            {
                text.Append(IsForbidden(name[i]) || char.IsSurrogate(name[i]) ? '?' : name[i]);
            }
        }

        return i < name.Length ? text.Append('…').ToString() : text.ToString();
    }

    /// <summary>A free name for a copy of <paramref name="source"/>: "Cool kopya", then "Cool kopya 2", ….</summary>
    /// <param name="source">The name of a profile in <paramref name="catalog"/>, so already valid.</param>
    public static string NextCopyName(string source, ProfileCatalog catalog)
    {
        var baseName = Normalize(source);
        for (var number = 1; ; number++)
        {
            var suffix = number == 1 ? CopySuffix : $"{CopySuffix} {number}";
            var candidate = Normalize(Shorten(baseName, MaxLength - suffix.Length) + suffix);
            if (!IsTaken(candidate, catalog, except: null))
            {
                return candidate;
            }
        }
    }

    private static bool IsTaken(string name, ProfileCatalog catalog, string? except) =>
        catalog.Profiles.Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(p.Name, except, StringComparison.OrdinalIgnoreCase));

    private static string Shorten(string name, int maxLength)
    {
        if (name.Length <= maxLength)
        {
            return name;
        }

        var cut = char.IsHighSurrogate(name[maxLength - 1]) ? maxLength - 1 : maxLength;
        return name[..cut].TrimEnd();
    }

    private static bool HasUnpairedSurrogate(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsSurrogatePair(name, i))
            {
                i++;
            }
            else if (char.IsSurrogate(name[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForbidden(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is
        UnicodeCategory.Control
        or UnicodeCategory.Format
        or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator;
}
