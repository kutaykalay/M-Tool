using System.Diagnostics;
using System.IO;
using MTool.App.Resources;
using MTool.App.Services;
using MTool.App.ViewModels;
using MTool.Core;

namespace MTool.App.Reports;

/// <summary>What the tray asks Windows to open: a folder in Explorer, the issue form or a project page in the browser.</summary>
internal interface IShellLauncher
{
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    void OpenFolder(string folder);

    /// <exception cref="ArgumentException">The address is neither the https issue form nor a <see cref="ProjectLinks"/> page of M-Tool.</exception>
    /// <exception cref="BrowserNotOpenedException">The desktop shell could not be reached, so the browser was not started.</exception>
    void OpenUrl(Uri url);
}

/// <summary>
/// The browser could only have been started from M-Tool itself, with its administrator rights, so it was
/// not started; the address was put on the clipboard instead, when that worked.
/// </summary>
internal sealed class BrowserNotOpenedException(bool addressCopied)
    : Exception("The desktop shell could not be reached, so the browser was not started.")
{
    public bool AddressCopied { get; } = addressCopied;
}

/// <summary>
/// The real launcher. The shell is given one path or one address and nothing else: no command line is
/// put together, and only the https issue form and project pages of M-Tool are ever opened. Both go through
/// the desktop's Explorer first (<see cref="DesktopShell"/>), so a browser started here does not get
/// M-Tool's administrator rights. When that shell cannot be reached, a folder is opened from M-Tool
/// itself and the log says so; an address is not opened at all but put on the clipboard.
/// </summary>
/// <param name="openUnelevated">Opens the target through the desktop shell; false when it cannot.</param>
/// <param name="openDirect">Opens the target from this (elevated) process.</param>
/// <param name="clipboard">Takes the address the browser was not started for.</param>
internal sealed class ShellLauncher(
    Func<string, bool> openUnelevated, Action<string> openDirect, IClipboard clipboard, IAppLog? log = null) : IShellLauncher
{
    public ShellLauncher(IAppLog? log = null)
        : this(DesktopShell.TryOpen, Start, new WpfClipboard(), log)
    {
    }

    /// <summary>What the warning band says when opening failed; never the reason itself, which can hold a path.</summary>
    public static string WarningFor(Exception exception) => exception switch
    {
        BrowserNotOpenedException { AddressCopied: true } => Strings.Shell_UrlCopied,
        BrowserNotOpenedException => Strings.Shell_UrlNotOpened,
        _ => Strings.Report_OpenFailed,
    };

    public void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The folder does not exist.");
        }

        var target = Path.GetFullPath(folder);
        if (openUnelevated(target))
        {
            return;
        }

        log?.Warn("Shell: the desktop shell could not be reached; folder opened from M-Tool, with its administrator rights.");
        openDirect(target);
    }

    public void OpenUrl(Uri url)
    {
        var address = BrowserStartInfo(url).FileName;
        if (openUnelevated(address))
        {
            return;
        }

        var copied = TryCopy(address);
        log?.Warn(copied
            ? "Shell: the desktop shell could not be reached; the browser was not started, the address was copied."
            : $"Shell: the desktop shell could not be reached and the clipboard failed; the browser was not started. Address: {address}");
        throw new BrowserNotOpenedException(copied);
    }

    /// <summary>
    /// The address is opened by the shell, so Windows picks the user's default browser. Only the
    /// validated https address of the issue form or of a <see cref="ProjectLinks"/> page is ever passed.
    /// Handing the address to explorer.exe as an argument opened the Documents folder from this elevated
    /// process, so that was dropped.
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

    private bool TryCopy(string address)
    {
        try
        {
            clipboard.SetText(address);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log?.Warn($"Shell: the address could not be put on the clipboard: {ex.GetType().Name}");
            return false;
        }
    }

    private static void Start(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
