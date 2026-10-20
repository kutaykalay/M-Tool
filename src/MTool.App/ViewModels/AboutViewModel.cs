using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Diagnostics;

namespace MTool.App.ViewModels;

/// <summary>Puts text on the Windows clipboard; may throw while another program holds it.</summary>
internal interface IClipboard
{
    void SetText(string text);
}

/// <summary>
/// The About tab (Aşama 14a): version, the device rows, the licenses and the fixed links. It never goes
/// to the network and never reads the EC: the device rows come from what the session already holds and
/// one SMBIOS read, made off the UI thread the first time the tab is loaded.
/// </summary>
/// <param name="appVersion">The informational version, e.g. <c>0.16.0-beta.1+448e0a5…</c>.</param>
/// <param name="access">The session's current access; read on every load, as a failed write locks it.</param>
/// <param name="readSystem">The report's SMBIOS read; may throw.</param>
/// <param name="pawnIo">The installed PawnIO version, or null.</param>
/// <param name="readLicenses">The embedded license texts, or null when they are missing; may throw.</param>
internal sealed partial class AboutViewModel(
    string appVersion,
    Func<DeviceAccess> access,
    Func<SystemInfo> readSystem,
    Func<Version?> pawnIo,
    Func<string?> readLicenses,
    IShellLauncher shell,
    IClipboard clipboard,
    StatusViewModel status,
    IAppLog log,
    string logFolder) : ObservableObject
{
    private const int ShortCommitLength = 7;

    private Task<(SystemInfo? System, Version? PawnIo)>? _fixedValues;
    private string? _licenses;

    /// <summary>The version without build metadata, then the short commit when the metadata is one.</summary>
    public string VersionText { get; } = string.Format(Strings.About_Version, DisplayVersion(appVersion));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    public partial IReadOnlyList<DeviceInfoLine> DeviceLines { get; private set; } = [];

    public string LicenseText => _licenses ??= ReadLicenses();

    /// <summary>
    /// SMBIOS and PawnIO are read once; the access part follows the session every time. Never throws: on
    /// a failure the rows stay as they were and the next load tries again.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            var (system, pawnIoVersion) = await (_fixedValues ??= Task.Run(ReadFixedValues));
            DeviceLines = DeviceInfo.Lines(system, access(), pawnIoVersion);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _fixedValues = null;
            log.Warn($"About: the device rows could not be made: {ex.GetType().Name}");
        }
    }

    internal static string DisplayVersion(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return informational;
        }

        var version = informational[..plus];
        var metadata = informational[(plus + 1)..];
        return metadata.Length >= ShortCommitLength && metadata.All(char.IsAsciiHexDigit)
            ? $"{version} ({metadata[..ShortCommitLength]})"
            : version;
    }

    [RelayCommand(CanExecute = nameof(HasDeviceLines))]
    private void Copy()
    {
        try
        {
            clipboard.SetText(DeviceInfo.ToText(DisplayVersion(appVersion), DeviceLines));
            status.ShowInfo(Strings.About_Copied);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"Device info could not be put on the clipboard: {ex.GetType().Name}");
            status.ShowWarning(Strings.About_CopyFailed);
        }
    }

    [RelayCommand]
    private void OpenGitHub() => Open(() => shell.OpenUrl(ProjectLinks.Repository), "the GitHub page");

    [RelayCommand]
    private void OpenReleases() => Open(() => shell.OpenUrl(ProjectLinks.Releases), "the releases page");

    [RelayCommand]
    private void OpenLogFolder() => Open(() => shell.OpenFolder(logFolder), "the log folder");

    private bool HasDeviceLines() => DeviceLines.Count > 0;

    private (SystemInfo?, Version?) ReadFixedValues()
    {
        SystemInfo? system = null;
        try
        {
            system = readSystem();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"About: SMBIOS could not be read: {ex.GetType().Name}");
        }

        try
        {
            return (system, pawnIo());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"About: the PawnIO version could not be read: {ex.GetType().Name}");
            return (system, null);
        }
    }

    private string ReadLicenses()
    {
        try
        {
            return readLicenses() ?? Strings.About_LicensesUnavailable;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"About: the license texts could not be read: {ex.GetType().Name}");
            return Strings.About_LicensesUnavailable;
        }
    }

    /// <summary>The message never holds the path or the exception text: either may name the user.</summary>
    private void Open(Action open, string what)
    {
        try
        {
            open();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error($"Could not open {what}", ex);
            status.ShowWarning(ShellLauncher.WarningFor(ex));
        }
    }
}
