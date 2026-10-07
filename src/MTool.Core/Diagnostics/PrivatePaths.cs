using System.Text.RegularExpressions;

namespace MTool.Core.Diagnostics;

/// <summary>
/// Replaces the user's profile folder (<c>C:\Users\&lt;name&gt;</c>) with <see cref="Placeholder"/>, so a
/// log can be attached to a public issue without the user's name in every path.
/// </summary>
/// <remarks>
/// Only paths are masked: a bare word that happens to equal the user name ("fan", "eco") stays, since
/// masking it would corrupt ordinary log lines. A folder that merely starts with the profile path
/// (<c>C:\Users\Kutay2</c>, <c>C:\Users\Kutay.old</c>) is someone else's and stays too. Case is ignored
/// the invariant way, which does not pair Turkish "ı" with "I". Only the spelling Windows reports for
/// the profile is matched: an 8.3 short form (<c>C:\Users\KUTAYK~1</c>) or a URL-encoded one
/// (<c>John%20Doe</c>) is not. M-Tool's own paths come from the same API (AppData, UserProfile) in the
/// long form, so this covers what M-Tool logs.
/// </remarks>
public sealed class PrivatePaths
{
    public const string Placeholder = "%UserProfile%";

    private const string SeparatorPattern = @"[\\/]+";

    // A name continues with a word character, a hyphen, or a dot followed by more name.
    private const string EndOfName = @"(?![\w-]|\.\w)";

    private static readonly char[] SeparatorChars = ['\\', '/'];

    private readonly Regex? _profile;

    public PrivatePaths(string? profileFolder)
    {
        _profile = BuildPattern(profileFolder);
    }

    public static PrivatePaths ForCurrentUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    // An evaluator, not a replacement pattern: the placeholder is written as is, "$" or not.
    public string Mask(string text) => _profile is null ? text : _profile.Replace(text, _ => Placeholder);

    private static Regex? BuildPattern(string? profileFolder)
    {
        if (string.IsNullOrWhiteSpace(profileFolder))
        {
            return null;
        }

        var folder = profileFolder.Trim();
        var parts = folder.Split(SeparatorChars, StringSplitOptions.RemoveEmptyEntries);

        // A drive root or a bare name would mask far more than the profile.
        if (parts.Length < 2)
        {
            return null;
        }

        var lead = folder[0] is '\\' or '/' ? SeparatorPattern : string.Empty;
        var pattern = lead + string.Join(SeparatorPattern, parts.Select(Regex.Escape)) + EndOfName;
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
