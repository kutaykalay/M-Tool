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

    public void OpenUrl(Uri url)
    {
        var allowed = url.IsAbsoluteUri
            && url.Scheme == Uri.UriSchemeHttps
            && url.Host == IssueUrl.Host
            && url.AbsolutePath == IssueUrl.FormPath;
        if (!allowed)
        {
            throw new ArgumentException("Only the GitHub issue form of M-Tool can be opened.", nameof(url));
        }

        Start(url.AbsoluteUri);
    }

    private static void Start(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
