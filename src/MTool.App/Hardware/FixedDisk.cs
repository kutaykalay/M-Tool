using System.IO;

namespace MTool.App.Hardware;

/// <summary>
/// Whether a file lies on a fixed local disk. Network, removable and optical drives can block for a long time or
/// be swapped by a local user, so the probe reads files only from fixed disks. Asking the drive type does not
/// touch the media (it is <c>GetDriveType</c> underneath).
/// </summary>
internal static class FixedDisk
{
    /// <param name="path">A drive-letter path (<c>X:\...</c>).</param>
    /// <exception cref="ArgumentException">The path does not start with a drive letter.</exception>
    public static bool Is(string path) => new DriveInfo(path[..1]).DriveType == DriveType.Fixed;
}
