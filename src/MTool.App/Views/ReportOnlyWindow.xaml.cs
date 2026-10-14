using System.Windows;
using MTool.App.ViewModels;

namespace MTool.App.Views;

/// <summary>
/// Shown instead of M-Tool on a laptop it cannot control yet (no WMI1): why, and the report actions, so
/// the owner can still send what M-Tool needs to learn the model. It never opens the port or the gateway;
/// a report reads the EC through WMI only, as on the command line. Closing it ends M-Tool.
/// </summary>
internal partial class ReportOnlyWindow : Window
{
    public ReportOnlyWindow(string problem, ReportViewModel report, StatusViewModel status, bool dark)
    {
        InitializeComponent();
        ProblemText.Text = problem;
        DataContext = new Context(report, status);
        SourceInitialized += (_, _) => TitleBar.ApplyTheme(this, dark);
    }

    /// <summary>What the bindings read: the report commands and the message band.</summary>
    internal sealed record Context(ReportViewModel Report, StatusViewModel Status);
}
