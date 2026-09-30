using Microsoft.Win32;

namespace MTool.App.Hardware;

internal static class PawnIoInstallation
{
    public const string InstallCommand = "winget install namazso.PawnIO";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    /// <summary>Installed PawnIO version from its uninstall entry, or null when it is not installed.</summary>
    public static Version? InstalledVersion()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(UninstallKey);
        return Version.TryParse(key?.GetValue("DisplayVersion") as string, out var version) ? version : null;
    }
}
