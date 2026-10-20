using System.Diagnostics;
using System.IO;
using MTool.Core;

namespace MTool.App.Reports;

/// <summary>What the tray asks Windows to open: a folder in Explorer, the issue form or a project page in the browser.</summary>
internal interface IShellLauncher
{
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    void OpenFolder(string folder);

    /// <exception cref="ArgumentException">The address is neither the https issue form nor a <see cref="ProjectLinks"/> page of M-Tool.</exception>
    void OpenUrl(Uri url);
}

/// <summary>
/// The real launcher. The shell is given one path or one address and nothing else: no command line is
/// put together, and only the https issue form and project pages of M-Tool are ever opened. Both go through
/// the desktop's Explorer first (<see cref="DesktopShell"/>), so a browser started here does not get
/// M-Tool's administrator rights; only when that shell cannot be reached is the target opened from
/// M-Tool itself, and the log says so.
/// </summary>
/// <param name="openUnelevated">Opens the target through the desktop shell; false when it cannot.</param>
/// <param name="openDirect">Opens the target from this (elevated) process.</param>
internal sealed class ShellLauncher(Func<string, bool> openUnelevated, Action<string> openDirect, IAppLog? log = null) : IShellLauncher
{
    public ShellLauncher(IAppLog? log = null)
        : this(DesktopShell.TryOpen, Start, log)
    {
    }

    public void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The folder does not exist.");
        }

        Open(Path.GetFullPath(folder));
    }

    public void OpenUrl(Uri url) => Open(BrowserStartInfo(url).FileName);

    /// <summary>
    /// The address is opened by the shell, so Windows picks the user's default browser. Only the
    /// validated https address of the issue form or of a <see cref="ProjectLinks"/> page is ever passed.
    /// This is the way from M-Tool itself, used only without the desktop shell. Handing the address to
    /// explorer.exe as an argument opened the Documents folder from this elevated process, so that was dropped.
    /// </summary>
    /// <exception cref="ArgumentException">The address is neither the issue form nor a project page of M-Tool.</exception>
    internal static ProcessStartInfo BrowserStartInfo(Uri url)
    {
        if (!IsIssueForm(url) && !ProjectLinks.IsAllowed(url))
        {
            throw new ArgumentException("Only the GitHub issue form and project pages of M-Tool can be opened.", nameof(url));
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

    private void Open(string target)
    {
        if (openUnelevated(target))
        {
            return;
        }

        log?.Warn("Shell: the desktop shell could not be reached; opened from M-Tool, with its administrator rights.");
        openDirect(target);
    }

    private static void Start(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
