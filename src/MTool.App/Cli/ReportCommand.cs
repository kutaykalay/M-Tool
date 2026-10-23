using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Localization;

namespace MTool.App.Cli;

/// <summary>Where each part of the report comes from; every call may throw, and the report goes on.</summary>
internal interface IReportSources
{
    SystemInfo ReadSystem();

    /// <summary>The probe itself never throws; it answers a failure with its step and code.</summary>
    WmiProbeOutcome ReadInterface();

    IReadOnlyList<WmiClassInfo> ReadClasses();

    /// <summary>Read-only: the MSI WMI schema registration and MSI's software; never throws for the system's sake.</summary>
    MsiInfo ReadMsi();

    byte[] ReadDsdt();

    /// <summary>Opens a WMI-only session: the raw port is never used for a report.</summary>
    EcReadout ReadEc();

    /// <summary>The <c>Get_*</c> WMI2 packets (<see cref="Wmi2Probe"/>); asked for with <c>--wmi2</c>.</summary>
    Wmi2Readout ReadWmi2();
}

/// <param name="Dsdt">Null when it could not be read.</param>
internal sealed record ReportData(DeviceReportInput Input, byte[]? Dsdt);

/// <summary>
/// <c>--report</c>: collects what M-Tool needs to learn another MSI model and writes it to a zip
/// the user attaches to a GitHub issue. Read-only. The EC is read only on WMI1 and only through
/// WMI; a model M-Tool cannot open still gets a report.
/// </summary>
internal static class ReportCommand
{
    public const string ReportFileName = "report.txt";

    private const string TempSuffix = ".tmp";

    /// <summary>E_FAIL, for an exception that carries no HRESULT of its own.</summary>
    private const uint EFail = 0x80004005;

    /// <summary>A broken character in a firmware string becomes U+FFFD; it must not fail the whole report.</summary>
    private static readonly UTF8Encoding LenientUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary><c>--report</c> or <c>--report --wmi2</c>.</summary>
    public static bool TryParse(string[] args, out bool wmi2)
    {
        (var ok, wmi2) = args switch
        {
            ["--report"] => (true, false),
            ["--report", "--wmi2"] => (true, true),
            _ => (false, false),
        };
        return ok;
    }

    /// <summary>Every part of the report; a part that cannot be read is written as such.</summary>
    /// <param name="warn">Gets every part that could not be read, for the log.</param>
    /// <param name="includeWmi2">Call the WMI2 <c>Get_*</c> methods; only ever on a WMI2 laptop.</param>
    /// <param name="includeDsdt">Read the DSDT (the zip needs it; the EC data submission does not send it).</param>
    public static ReportData Collect(
        IReportSources sources, string appVersion, DateTimeOffset at, Action<string> warn, bool includeWmi2 = false, bool includeDsdt = true)
    {
        var wmi = ReadInterface(sources, warn);
        var dsdt = includeDsdt ? Read("DSDT", sources.ReadDsdt, warn) : Section.Failed<byte[]>("not read: not needed");
        var ec = wmi.Status != WmiProbeStatus.Ok ? Section.Failed<EcReadout>("not read: MSI WMI interface unknown")
            : wmi.Interface switch
            {
                WmiInterface.Wmi1 => Read("EC", sources.ReadEc, warn),
                WmiInterface.Wmi2 => Section.Failed<EcReadout>("not read: M-Tool reads the EC only through WMI1 so far"),
                _ => Section.Failed<EcReadout>("not read: no MSI WMI interface on this computer"),
            };

        var input = new DeviceReportInput(
            appVersion,
            at,
            Read("system information", sources.ReadSystem, warn),
            wmi,
            Read("MSI classes", sources.ReadClasses, warn),
            ReadMsi(sources, warn),
            dsdt.IsOk ? Section.Ok(dsdt.Value!.Length) : Section.Failed<int>(dsdt.Error!),
            ec,
            includeWmi2 ? Wmi2(sources, wmi, warn) : null);
        return new ReportData(input, dsdt.Value);
    }

    private static Section<Wmi2Readout> Wmi2(IReportSources sources, WmiProbeOutcome wmi, Action<string> warn) =>
        wmi is { Status: WmiProbeStatus.Ok, Interface: WmiInterface.Wmi2 }
            ? Read("WMI2 packets", sources.ReadWmi2, warn)
            : Section.Failed<Wmi2Readout>(wmi.Status == WmiProbeStatus.Ok
                ? "not read: this computer has no WMI2 interface"
                : "not read: MSI WMI interface unknown");

    /// <summary>A source that throws all the same (a bug) is written as a failed connect with the exception's HRESULT.</summary>
    private static WmiProbeOutcome ReadInterface(IReportSources sources, Action<string> warn)
    {
        WmiProbeOutcome probe;
        try
        {
            probe = sources.ReadInterface();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            warn($"Report: MSI WMI interface unreadable: {ex.ForLog()}");
            return WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.Connect, ex.HResult == 0 ? EFail : unchecked((uint)ex.HResult)));
        }

        if (probe.Failure is { } failure)
        {
            warn($"Report: MSI WMI interface unknown: {failure}");
        }

        return probe;
    }

    private static MsiInfo ReadMsi(IReportSources sources, Action<string> warn)
    {
        try
        {
            return sources.ReadMsi();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            warn($"Report: MSI schema and software unreadable: {ex.ForLog()}");
            return MsiInfo.Unknown;
        }
    }

    /// <returns>The zip's path.</returns>
    /// <exception cref="IOException">The zip could not be written (it already exists, the disk is full); no partial file is left.</exception>
    public static string Save(string folder, ReportData data, string text, DateTimeOffset at)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"m-tool-report-{at:yyyyMMdd-HHmmss}.zip"));
        WriteNew(path, file => Write(file, data, text));
        return path;
    }

    /// <summary>
    /// Writes to a temporary name next to <paramref name="path"/>, then renames it, so the final name
    /// never holds a half-written file. An existing file is never replaced. Nothing is left behind on failure.
    /// </summary>
    /// <exception cref="IOException">The file exists, or the disk refused.</exception>
    internal static void WriteNew(string path, Action<Stream> write)
    {
        var temp = path + TempSuffix;
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                write(file);
            }

            File.Move(temp, path); // fails when path exists: no overwrite
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Cleaning up must not hide the error that is being thrown.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort: the original failure is what the caller needs to see
        }
    }

    private static void Write(Stream file, ReportData data, string text)
    {
        using var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        using (var writer = new StreamWriter(zip.CreateEntry(ReportFileName).Open(), LenientUtf8))
        {
            writer.Write(text);
        }

        if (data.Dsdt is { } dsdt)
        {
            using var stream = zip.CreateEntry(DeviceReport.DsdtFileName).Open();
            stream.Write(dsdt);
        }
    }

    /// <summary>Anything but running out of memory becomes a failed part: the report itself must not fail for the laptop's sake.</summary>
    private static Section<T> Read<T>(string part, Func<T> read, Action<string> warn)
    {
        try
        {
            return Section.Ok(read());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            warn($"Report: {part} unreadable: {ex.ForLog()}");
            return Section.Failed<T>($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
