using System.ComponentModel;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Win32;
using MTool.Core.Diagnostics;

namespace MTool.App.Hardware;

/// <summary>
/// The system reads <see cref="MsiSoftwareProbe"/> decides on, so tests never touch the real registry
/// or processes. A source may throw: the probe treats that as "could not be read".
/// </summary>
/// <param name="ReadMofImagePath">The <c>MofImagePath</c> value (not expanded), or null when it is not there.</param>
/// <param name="WindowsDirectory">The Windows directory, the only thing a <c>%SystemRoot%</c> form is expanded to (never the process environment).</param>
/// <param name="FileExists">Whether the file is there.</param>
/// <param name="ReadSignerSubject">The signer's subject, or null when the file has no signature.</param>
/// <param name="ReadPublishers">The <c>Publisher</c> of every Uninstall entry, one list per hive (null = that hive could not be read).</param>
/// <param name="ReadProcessScan">The file company of every running program, within the time budget (null = no scan could be made).</param>
internal sealed record MsiSoftwareSources(
    Func<string?> ReadMofImagePath,
    Func<string> WindowsDirectory,
    Func<string, bool> FileExists,
    Func<string, string?> ReadSignerSubject,
    Func<IReadOnlyList<IReadOnlyList<string?>?>> ReadPublishers,
    Func<ProcessScanResult?> ReadProcessScan);

/// <summary>
/// Looks, read-only, for what MSI's WMI interface needs: the schema DLL Windows was told about, and
/// MSI's own software. MSI software is recognised by vendor metadata only (Uninstall publisher, file
/// company), never by guessed process or service names. Nothing is written or started, no WMI is
/// called and the EC is never touched. System trouble gives "unknown", never an exception.
/// </summary>
internal sealed class MsiSoftwareProbe(MsiSoftwareSources sources)
{
    private const string WmiAcpiKey = @"SYSTEM\CurrentControlSet\Services\WmiAcpi";
    private const string MofImagePathValue = "MofImagePath";
    private const string PublisherValue = "Publisher";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKeyWow64 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Looking at running programs can be slow; the whole process scan stops after this.</summary>
    private static readonly TimeSpan ProcessScanBudget = TimeSpan.FromSeconds(3);

    public static MsiSoftwareProbe Default { get; } = new(new MsiSoftwareSources(
        ReadMofImagePathFromRegistry,
        () => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        File.Exists,
        AuthenticodeSigner.ReadSubject,
        () => [ReadPublishersOf(UninstallKey), ReadPublishersOf(UninstallKeyWow64)],
        () => ProcessImageScan.RunOnThisMachine(ProcessScanBudget)));

    /// <summary>
    /// The schema facts. Narrow filters below give the better answer where the cause is known; any other
    /// non-fatal exception still ends in "unknown" rather than reaching the caller.
    /// </summary>
    public MsiSchemaInfo ReadSchema()
    {
        try
        {
            return ReadSchemaCore();
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return new MsiSchemaInfo(MofImagePathState.Unreadable, SchemaFileState.Unknown, SchemaSignature.Unknown);
        }
    }

    public MsiSoftwareInfo ReadSoftware() => new(Installed(), Running());

    /// <summary>Out of memory or a corrupted process: nothing the probe may answer around.</summary>
    internal static bool IsFatal(Exception ex) => ex is OutOfMemoryException or AccessViolationException or StackOverflowException;

    /// <summary>Reads one value per name; a name that cannot be read adds a null entry and the rest are still read.</summary>
    internal static IReadOnlyList<string?> ReadEntries(IEnumerable<string> names, Func<string, string?> readValue)
    {
        var values = new List<string?>();
        foreach (var name in names)
        {
            try
            {
                values.Add(readValue(name));
            }
            catch (Exception ex) when (IsSystemTrouble(ex))
            {
                values.Add(null);
            }
        }

        return values;
    }

    private MsiSchemaInfo ReadSchemaCore()
    {
        string? raw;
        try
        {
            raw = sources.ReadMofImagePath();
        }
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return new MsiSchemaInfo(MofImagePathState.Unreadable, SchemaFileState.Unknown, SchemaSignature.Unknown);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new MsiSchemaInfo(MofImagePathState.NotSet, SchemaFileState.Unknown, SchemaSignature.Unknown);
        }

        string? path;
        bool exists;
        try
        {
            path = MofImagePathResolver.Resolve(raw, sources.WindowsDirectory());
            if (path is null)
            {
                return new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Unknown, SchemaSignature.Unknown);
            }

            exists = sources.FileExists(path);
        }
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Unknown, SchemaSignature.Unknown);
        }

        return exists
            ? new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Exists, SignatureOf(path))
            : new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Missing, SchemaSignature.Unknown);
    }

    private SchemaSignature SignatureOf(string path)
    {
        try
        {
            return MsiSchemaInfo.ClassifySignature(sources.ReadSignerSubject(path));
        }
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return SchemaSignature.Unknown;
        }
    }

    private bool? Installed()
    {
        try
        {
            return MsiSoftwareInfo.Decide(sources.ReadPublishers()
                .Select(hive => hive is null ? (bool?)null : hive.Any(MsiSoftwareInfo.IsMicroStar)));
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return null;
        }
    }

    private bool? Running()
    {
        try
        {
            return sources.ReadProcessScan() is { } scan
                ? MsiSoftwareInfo.Decide(
                    scan.Companies.Select(company => company is null ? (bool?)null : MsiSoftwareInfo.IsMicroStar(company)),
                    scan.Truncated)
                : null;
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return null;
        }
    }

    /// <summary>What a registry, file or process read throws for reasons outside M-Tool.</summary>
    private static bool IsSystemTrouble(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException
        or InvalidOperationException or Win32Exception or CryptographicException or ArgumentException or NotSupportedException;

    private static string? ReadMofImagePathFromRegistry()
    {
        using var key = Registry.LocalMachine.OpenSubKey(WmiAcpiKey);
        return key?.GetValue(MofImagePathValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static IReadOnlyList<string?>? ReadPublishersOf(string uninstallKey)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(uninstallKey);
            return root is null ? null : ReadEntries(root.GetSubKeyNames(), name => ReadPublisher(root, name));
        }
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return null;
        }
    }

    private static string? ReadPublisher(RegistryKey root, string name)
    {
        using var entry = root.OpenSubKey(name);
        return entry?.GetValue(PublisherValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
}
