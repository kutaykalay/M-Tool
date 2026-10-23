namespace MTool.Core.Diagnostics;

/// <summary>
/// Turns the raw <c>MofImagePath</c> registry text into a full local path without asking the
/// environment: the process runs elevated, and a variable such as <c>%TEMP%</c> or <c>%ProgramData%</c>
/// can be changed by a local user, so only the Windows directory (passed in) is substituted. Pure:
/// nothing is read from the disk or the environment.
/// </summary>
public static class MofImagePathResolver
{
    private static readonly string[] PercentRoots = ["%SystemRoot%", "%windir%"];
    private const string NtRoot = @"\SystemRoot\";

    /// <summary>
    /// The full path, or null when the text cannot be trusted as a local file path: another
    /// <c>%</c> variable is left, the path is relative, UNC, a device (<c>\\?\</c>, <c>\\.\</c>) or not
    /// on a drive letter, or names an alternate data stream. A null answer means no file may be touched.
    /// </summary>
    /// <param name="raw">The registry text, not expanded.</param>
    /// <param name="windowsDirectory">The Windows directory (what <c>%SystemRoot%</c> stands for).</param>
    public static string? Resolve(string raw, string windowsDirectory)
    {
        var text = raw.Trim();
        var expanded = ExpandRoot(text, windowsDirectory.TrimEnd('\\', '/'));
        if (expanded is null || !IsLocalDrivePath(expanded) || expanded.Contains('%') || expanded.IndexOf(':', 2) >= 0)
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(expanded);
            return Path.IsPathFullyQualified(full) && IsLocalDrivePath(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }

    /// <summary>True for <c>X:\...</c> (or <c>X:/...</c>); false for relative, UNC and device paths.</summary>
    public static bool IsLocalDrivePath(string? path) =>
        path is { Length: >= 3 }
        && char.IsAsciiLetter(path[0])
        && path[1] == ':'
        && (path[2] == '\\' || path[2] == '/');

    private static string? ExpandRoot(string text, string windowsDirectory)
    {
        foreach (var root in PercentRoots)
        {
            if (text.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return JoinRoot(windowsDirectory, text[root.Length..]);
            }
        }

        return text.StartsWith(NtRoot, StringComparison.OrdinalIgnoreCase)
            ? JoinRoot(windowsDirectory, text[(NtRoot.Length - 1)..])
            : text;
    }

    /// <summary>The root form must be followed by a separator ("%windir%x" would name a sibling directory).</summary>
    private static string? JoinRoot(string windowsDirectory, string rest) =>
        IsLocalDrivePath(windowsDirectory + "\\") && rest.Length > 0 && (rest[0] == '\\' || rest[0] == '/')
            ? windowsDirectory + rest
            : null;
}
