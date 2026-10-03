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
