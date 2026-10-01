using System.IO;
using MTool.App.Startup;

namespace MTool.App.Services;

/// <summary>What the sign-in task looks like, and the path rules around it. No Task Scheduler calls.</summary>
internal static class StartupTaskSpec
{
    /// <summary>In the root folder of Task Scheduler.</summary>
    public const string TaskName = "M-Tool";

    public const string Arguments = StartupArgs.Tray;

    /// <summary>Lets WMI and PawnIO come up after sign-in before the EC session opens.</summary>
    public static readonly TimeSpan SignInDelay = TimeSpan.FromSeconds(10);

    /// <summary>True when the task starts another exe than <paramref name="currentExe"/>, none, or an unreadable path.</summary>
    public static bool NeedsRepair(string? registeredExe, string currentExe) =>
        Normalize(registeredExe) is not { } registered
        || !string.Equals(registered, Normalize(currentExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The task runs elevated without a UAC prompt, so the exe should sit where only administrators
    /// can replace it. Lexical only: junctions, links, 8.3 names and the folder's ACL are not checked
    /// (the ACL of the install folder is checked by hand when installing). A warning, not a gate.
    /// </summary>
    public static bool IsTrustedLocation(string exePath, string programFiles)
    {
        if (Normalize(exePath) is not { } exe || Normalize(programFiles) is not { } folder)
        {
            return false;
        }

        return exe.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Full path, or null for an empty or invalid one (a task path is outside data).</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
