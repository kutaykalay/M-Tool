using System.ComponentModel;
using System.IO;
using System.Diagnostics;
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
/// <param name="Expand">Expands environment variables such as <c>%windir%</c>.</param>
/// <param name="FileExists">Whether the file is there.</param>
/// <param name="ReadSignerSubject">The signer's subject, or null when the file has no signature.</param>
/// <param name="ReadPublishers">The <c>Publisher</c> of every Uninstall entry, one list per hive (null = that hive could not be read).</param>
/// <param name="ReadCompanies">The file company of every running program (null entry = that program could not be read; null list = none could).</param>
internal sealed record MsiSoftwareSources(
    Func<string?> ReadMofImagePath,
    Func<string, string> Expand,
    Func<string, bool> FileExists,
    Func<string, string?> ReadSignerSubject,
    Func<IReadOnlyList<IReadOnlyList<string?>?>> ReadPublishers,
    Func<IReadOnlyList<string?>?> ReadCompanies);

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
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKeyWow64 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Reading a process's main module can be slow; the whole process scan stops after this.</summary>
    private static readonly TimeSpan ProcessScanBudget = TimeSpan.FromSeconds(3);

    public static MsiSoftwareProbe Default { get; } = new(new MsiSoftwareSources(
        ReadMofImagePathFromRegistry,
        Environment.ExpandEnvironmentVariables,
        File.Exists,
        AuthenticodeSigner.ReadSubject,
        () => [ReadPublishersOf(UninstallKey), ReadPublishersOf(UninstallKeyWow64)],
        ReadProcessCompanies));

    public MsiSchemaInfo ReadSchema()
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

        string path;
        bool exists;
        try
        {
            path = sources.Expand(raw);
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

    public MsiSoftwareInfo ReadSoftware() => new(Installed(), Running());

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
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return null;
        }
    }

    private bool? Running()
    {
        try
        {
            return sources.ReadCompanies() is { } companies
                ? MsiSoftwareInfo.Decide(companies.Select(company => company is null ? (bool?)null : MsiSoftwareInfo.IsMicroStar(company)))
                : null;
        }
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return null;
        }
    }

    /// <summary>What a registry, file or process read throws for reasons outside M-Tool; anything else is a bug and surfaces.</summary>
    private static bool IsSystemTrouble(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException
        or InvalidOperationException or Win32Exception or CryptographicException or ArgumentException or NotSupportedException;

    private static string? ReadMofImagePathFromRegistry()
    {
        using var key = Registry.LocalMachine.OpenSubKey(WmiAcpiKey);
        return key?.GetValue(MofImagePathValue) as string;
    }

    private static IReadOnlyList<string?>? ReadPublishersOf(string uninstallKey)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(uninstallKey);
            if (root is null)
            {
                return null;
            }

            var publishers = new List<string?>();
            foreach (var name in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(name);
                publishers.Add(entry?.GetValue("Publisher") as string);
            }

            return publishers;
        }
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return null;
        }
    }

    /// <summary>Services run as processes too. A process whose main module cannot be read (access denied, exited meanwhile, 32/64-bit) is skipped.</summary>
    private static IReadOnlyList<string?>? ReadProcessCompanies()
    {
        var clock = Stopwatch.StartNew();
        var companies = new List<string?>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (clock.Elapsed > ProcessScanBudget)
                {
                    break;
                }

                companies.Add(CompanyOf(process));
            }
        }

        return companies;
    }

    private static string? CompanyOf(Process process)
    {
        try
        {
            return process.MainModule?.FileVersionInfo.CompanyName ?? string.Empty;
        }
        catch (Exception ex) when (IsSystemTrouble(ex))
        {
            return null;
        }
    }
}
