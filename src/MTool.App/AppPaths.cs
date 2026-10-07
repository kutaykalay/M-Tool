using System.IO;

namespace MTool.App;

internal static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M-Tool");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    public static string Dumps { get; } = Path.Combine(Root, "dumps");

    public static string Reports { get; } = Path.Combine(Root, "reports");
}
