using System.Windows.Forms;
using MTool.App.Resources;
using MTool.App.ViewModels;
using static MTool.App.Tray.TrayDeviceItems;

namespace MTool.App.Tray;

/// <summary>
/// The device report part of the tray menu: send the EC data, the plain report, the WMI2 report (only
/// on a laptop that has WMI2), the folder of the last report (once there is one) and the issue form.
/// None of them touches the EC through the gateway, so all stay enabled while writing is locked; all but
/// the folder wait while a report is being made or sent.
/// </summary>
internal static class TrayReportItems
{
    public static IReadOnlyList<ToolStripItem> For(ReportViewModel report)
    {
        var items = new List<ToolStripItem>
        {
            Entry(Strings.Report_SendEcData, report.SendEcDataCommand.CanExecute(null), () => Run(report.SendEcDataCommand, null)),
            Entry(Strings.Report_Create, report.CreateCommand.CanExecute(null), () => Run(report.CreateCommand, null)),
        };
        if (report.HasWmi2)
        {
            items.Add(Entry(Strings.Report_CreateWmi2, report.CreateWithWmi2Command.CanExecute(null),
                () => Run(report.CreateWithWmi2Command, null)));
        }

        if (report.LastReportPath is not null)
        {
            items.Add(Entry(Strings.Report_OpenFolder, enabled: true, () => Run(report.OpenFolderCommand, null)));
        }

        items.Add(Entry(Strings.Report_IssueMenu, report.ReportIssueCommand.CanExecute(null), () => Run(report.ReportIssueCommand, null)));
        return items.AsReadOnly();
    }

    private static ToolStripMenuItem Entry(string text, bool enabled, Action onClick) =>
        Item(text.Replace("&", "&&", StringComparison.Ordinal), isChecked: false, enabled, onClick);
}
