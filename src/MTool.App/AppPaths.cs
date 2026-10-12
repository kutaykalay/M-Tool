using System.IO;

namespace MTool.App;

internal static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M-Tool");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    public static string Dumps { get; } = Path.Combine(Root, "dumps");

    public static string Reports { get; } = Path.Combine(Root, "reports");

    /// <summary>
    /// <see cref="Reports"/> as it is shown to the user: the profile folder holds the user name, which
    /// is kept out of balloons and status messages (the log has the full path).
    /// </summary>
    public static string ReportsShown { get; } = Path.Combine("%AppData%", "M-Tool", "reports");
}
