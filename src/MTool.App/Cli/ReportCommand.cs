using System.Globalization;
using System.IO;
using System.IO.Compression;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.App.Cli;

/// <summary>Where each part of the report comes from; every call may throw, and the report goes on.</summary>
internal interface IReportSources
{
    SystemInfo ReadSystem();

    WmiInterface? ReadInterface();

    IReadOnlyList<WmiClassInfo> ReadClasses();

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
    public static ReportData Collect(IReportSources sources, string appVersion, DateTimeOffset at, Action<string> warn, bool includeWmi2 = false)
    {
        var wmi = Read("MSI WMI interface", sources.ReadInterface, warn);
        var dsdt = Read("DSDT", sources.ReadDsdt, warn);
        var ec = !wmi.IsOk ? Section.Failed<EcReadout>("not read: MSI WMI interface unknown")
            : wmi.Value switch
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
            dsdt.IsOk ? Section.Ok(dsdt.Value!.Length) : Section.Failed<int>(dsdt.Error!),
            ec,
            includeWmi2 ? Wmi2(sources, wmi, warn) : null);
        return new ReportData(input, dsdt.Value);
    }

    private static Section<Wmi2Readout> Wmi2(IReportSources sources, Section<WmiInterface?> wmi, Action<string> warn) =>
        wmi is { IsOk: true, Value: WmiInterface.Wmi2 }
            ? Read("WMI2 paketleri", sources.ReadWmi2, warn)
            : Section.Failed<Wmi2Readout>(wmi.IsOk ? "not read: this computer has no WMI2 interface" : "not read: MSI WMI interface unknown");

    /// <returns>The zip's path.</returns>
    /// <exception cref="IOException">The zip could not be written (it already exists, the disk is full); no partial file is left.</exception>
    public static string Save(string folder, ReportData data, string text, DateTimeOffset at)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"m-tool-report-{at:yyyyMMdd-HHmmss}.zip"));
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            try
            {
                Write(file, data, text);
            }
            catch
            {
                file.Dispose();
                File.Delete(path);
                throw;
            }
        }

        return path;
    }

    private static void Write(Stream file, ReportData data, string text)
    {
        using var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        using (var writer = new StreamWriter(zip.CreateEntry(ReportFileName).Open()))
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
            warn($"Report: {part} unreadable: {ex.Message}");
            return Section.Failed<T>($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
