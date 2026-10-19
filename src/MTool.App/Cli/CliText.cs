using MTool.App.Resources;
using MTool.App.Startup;

namespace MTool.App.Cli;

/// <summary>
/// What the command line says to the user, in the Windows language. The log and the <c>--dump</c>,
/// <c>--watch</c> and <c>--apply</c> report lines are technical and stay as they are.
/// </summary>
internal static class CliText
{
    public static string Usage() =>
        string.Join(Environment.NewLine,
            Strings.Cli_UsageHeader,
            string.Format(Strings.Cli_UsageGui, StartupArgs.Tray),
            "  M-Tool.exe --dump",
            Strings.Cli_UsageReport,
            WatchCommand.Usage,
            ApplyCommand.Usage);

    public static string WatchUsage => Strings.Cli_UsageWatch;

    public static string ApplyUsage => Strings.Cli_UsageApply;

    public static string Saved(string path) => string.Format(Strings.Cli_Saved, path);

    public static string ReportGuidance() => Strings.Cli_ReportGuidance;

    public static string NoWriteLock() => Strings.Cli_NoWriteLock;

    public static string LockRemoved(string reason) => string.Format(Strings.Cli_LockRemoved, reason);

    public static string Error(string message) => string.Format(Strings.Cli_Error, message);

    public static string Details(string logFolder) => string.Format(Strings.Cli_ErrorDetails, logFolder);

    public static string DumpAfterWriteFailed(string message) => string.Format(Strings.Cli_DumpAfterWriteFailed, message);
}
