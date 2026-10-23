using System.IO;
using MTool.App.Cli;
using MTool.Core;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Localization;

namespace MTool.App.Reports;

/// <param name="Path">The zip.</param>
/// <param name="Text">The text of report.txt, names already hidden.</param>
/// <param name="Model">The laptop model from SMBIOS; null when unread or when it holds a private name.</param>
/// <param name="Firmware">The EC firmware version; null when unread or when it holds a private name.</param>
internal sealed record ReportResult(string Path, string Text, string? Model, string? Firmware);

/// <summary>
/// The device report for both the command line and the tray: collects, hides the user and computer
/// names, saves the zip. Read-only. Blocks for a few seconds, so the tray calls it off the UI thread.
/// Which report it makes is chosen by the method, not by a flag: <see cref="Create"/> never asks WMI2
/// for anything, only <see cref="CreateWithWmi2"/> does (and only on a WMI2 laptop).
/// </summary>
internal sealed class ReportService(
    IReportSources sources, IAppLog log, string folder, string appVersion, IReadOnlyList<string> privateWords, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The names that must not appear in a report: the user, the computer, the profile folder.</summary>
    public static IReadOnlyList<string> CurrentUserWords()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [Environment.UserName, Environment.MachineName, Environment.UserDomainName, profile, Path.GetFileName(profile)];
    }

    /// <summary>Whether this laptop has the WMI2 interface, from the same probe the report uses. Reads no EC and calls no WMI2 method.</summary>
    public bool HasWmi2Interface()
    {
        try
        {
            var probe = sources.ReadInterface();
            if (probe.Failure is { } failure)
            {
                log.Warn($"Report: could not tell whether this laptop has WMI2: {failure}");
            }

            return probe.Interface == WmiInterface.Wmi2;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"Report: could not tell whether this laptop has WMI2: {ex.ForLog()}");
            return false;
        }
    }

    /// <summary>The report with WMI1 only.</summary>
    /// <exception cref="IOException">The zip could not be written; no partial file is left.</exception>
    public ReportResult Create() => Run(includeWmi2: false);

    /// <summary>The report with the raw WMI2 <c>Get_*</c> packets, once, and only on a WMI2 laptop.</summary>
    /// <exception cref="IOException">The zip could not be written; no partial file is left.</exception>
    public ReportResult CreateWithWmi2() => Run(includeWmi2: true);

    /// <summary>
    /// The text "Send EC data" shows and then sends (<see cref="EcSubmission"/>): the same parts as the
    /// report but no DSDT, and nothing written to disk. WMI2 is called only when asked for, and only on a
    /// WMI2 laptop; the caller asks the user first.
    /// </summary>
    public string CollectForSubmission(bool includeWmi2)
    {
        var data = ReportCommand.Collect(sources, appVersion, _time.GetLocalNow(), log.Warn, includeWmi2, includeDsdt: false);
        return EcSubmission.ToJson(data.Input, privateWords);
    }

    private ReportResult Run(bool includeWmi2)
    {
        var at = _time.GetLocalNow();
        var data = ReportCommand.Collect(sources, appVersion, at, log.Warn, includeWmi2);
        var text = DeviceReport.Format(data.Input, privateWords);
        var path = ReportCommand.Save(folder, data, text, at);
        log.Info($"Report written: {path}");
        return new ReportResult(path, text, Safe(data.Input.Computer.Value?.Model), Safe(data.Input.Ec.Value?.Firmware?.Version));
    }

    /// <summary>The value, or null when it is blank or holds a private name (see <see cref="PrivateText.HoldsPrivateName"/>).</summary>
    private string? Safe(string? value) =>
        string.IsNullOrWhiteSpace(value) || PrivateText.HoldsPrivateName(value, privateWords) ? null : value.Trim();
}
