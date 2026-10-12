using System.Diagnostics;
using System.IO;

namespace MTool.App.Reports;

/// <summary>What the tray asks Windows to open: a folder in Explorer, the issue form in the browser.</summary>
internal interface IShellLauncher
{
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    void OpenFolder(string folder);

    /// <exception cref="ArgumentException">The address is not the https issue form of M-Tool on GitHub.</exception>
    void OpenUrl(Uri url);
}

/// <summary>
/// The real launcher. The shell is given one path or one address and nothing else: no command line is
/// put together, and only the https address of M-Tool's own issue form is ever opened.
/// </summary>
internal sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The folder does not exist.");
        }

        Start(Path.GetFullPath(folder));
    }

    public void OpenUrl(Uri url) => Process.Start(BrowserStartInfo(url))?.Dispose();

    /// <summary>
    /// The address is opened by the shell, so Windows picks the user's default browser. Only the
    /// validated https address of the issue form is ever passed. Handing the address to explorer.exe
    /// instead (to keep the browser unelevated) opened the Documents folder from this elevated
    /// process, so that was dropped; a browser that is already running takes the address over itself.
    /// </summary>
    /// <exception cref="ArgumentException">The address is not the issue form of M-Tool.</exception>
    internal static ProcessStartInfo BrowserStartInfo(Uri url)
    {
        if (!IsIssueForm(url))
        {
            throw new ArgumentException("Only the GitHub issue form of M-Tool can be opened.", nameof(url));
        }

        return new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true };
    }

    /// <summary>
    /// Exactly what <see cref="IssueUrl.Build"/> makes: https, github.com, the default port, no user
    /// name, no fragment, the fixed path, and only the known query keys.
    /// </summary>
    internal static bool IsIssueForm(Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == Uri.UriSchemeHttps
        && url.UserInfo.Length == 0
        && url.IsDefaultPort
        && url.Fragment.Length == 0
        && string.Equals(url.Host, IssueUrl.Host, StringComparison.Ordinal)
        && string.Equals(url.AbsolutePath, IssueUrl.FormPath, StringComparison.Ordinal)
        && HasOnlyKnownFields(url.Query);

    private static bool HasOnlyKnownFields(string query) =>
        query.Length == 0
        || query[1..].Split('&').All(pair => IssueUrl.FieldNames.Contains(pair.Split('=', 2)[0], StringComparer.Ordinal));

    private static void Start(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
