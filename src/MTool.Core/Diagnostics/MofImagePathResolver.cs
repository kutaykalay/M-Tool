using System.Text.RegularExpressions;

namespace MTool.Core.Diagnostics;

/// <summary>
/// Turns the raw <c>MofImagePath</c> registry text into a full local path without asking the
/// environment: the process runs elevated, and a variable such as <c>%TEMP%</c> or <c>%ProgramData%</c>
/// can be changed by a local user, so only the Windows directory (passed in) is substituted. The environment is
/// never read. The result is only a candidate: <see cref="Path.GetFullPath(string)"/> may consult the file system
/// (it can expand 8.3 short names), and <c>..</c> segments can leave the Windows folder. Both are harmless for a
/// report that only looks at the file; the caller still has to check the drive type before touching it.
/// </summary>
public static partial class MofImagePathResolver
{
    private static readonly string[] PercentRoots = ["%SystemRoot%", "%windir%"];
    private const string NtRoot = @"\SystemRoot\";

    /// <summary>
    /// The full path, or null when the text cannot be trusted as a local file path: another
    /// <c>%</c> variable is left, the path is relative, UNC, a device (<c>\\?\</c>, <c>\\.\</c>) or not
    /// on a drive letter, names an alternate data stream or ends in a reserved device name (CON, NUL, COM1...). A null answer means no file may be touched.
    /// </summary>
    /// <param name="raw">The registry text, not expanded.</param>
    /// <param name="windowsDirectory">The Windows directory (what <c>%SystemRoot%</c> stands for).</param>
    public static string? Resolve(string raw, string windowsDirectory)
    {
        var text = raw.Trim();
        var expanded = ExpandRoot(text, windowsDirectory.TrimEnd('\\', '/'));
        if (expanded is null || !IsLocalDrivePath(expanded) || expanded.Contains('%') || expanded.IndexOf(':', 2) >= 0 || IsReservedDeviceName(expanded))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(expanded);
            return Path.IsPathFullyQualified(full) && IsLocalDrivePath(full) && !IsReservedDeviceName(full) ? full : null;
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

    /// <summary>
    /// True when the last segment is a DOS device name, with or without an extension and trailing dots or spaces
    /// (<c>con</c>, <c>NUL.txt</c>, <c>com1.</c>): opening it would talk to a device, not a file.
    /// </summary>
    private static bool IsReservedDeviceName(string path)
    {
        var segment = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        var dot = segment.IndexOf('.');
        var name = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');
        return ReservedDeviceName().IsMatch(name);
    }

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDeviceName();

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
