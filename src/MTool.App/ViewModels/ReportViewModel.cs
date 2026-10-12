using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.Core;

namespace MTool.App.ViewModels;

/// <summary>
/// The tray's device report. Collecting blocks for seconds, so it runs off the UI thread; one report at
/// a time. It needs neither EC access nor an unlocked write mode: it never goes through the gateway.
/// The plain report never asks WMI2 for anything. The WMI2 report is offered only on a laptop that has
/// the interface, and only after the user answered the question that says what will be read.
/// Messages shown to the user never hold a path from an error or a user name (the log has the details).
/// </summary>
internal sealed partial class ReportViewModel(
    ReportService service, IConfirm confirm, IShellLauncher shell, StatusViewModel status, INotifier notifier, IAppLog log) : ObservableObject
{
    private const string AppName = "M-Tool";

    private ReportResult? _last;

    /// <summary>True while a report is being made or the WMI2 question is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateWithWmi2Command))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Unknown (false) until <see cref="LoadAsync"/> has answered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateWithWmi2Command))]
    public partial bool HasWmi2 { get; private set; }

    /// <summary>The zip of the last report made since M-Tool started; null before the first.</summary>
    public string? LastReportPath => _last?.Path;

    /// <summary>Asks the interface probe (a WMI class check) whether the laptop has WMI2, off the UI thread. Reads no EC.</summary>
    public async Task LoadAsync() => HasWmi2 = await Task.Run(service.HasWmi2Interface);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task CreateAsync() => MakeAsync(service.Create, askFirst: false);

    [RelayCommand(CanExecute = nameof(CanStartWmi2))]
    private Task CreateWithWmi2Async() => MakeAsync(service.CreateWithWmi2, askFirst: true);

    [RelayCommand]
    private void OpenFolder()
    {
        if (LastReportPath is { } path && Path.GetDirectoryName(path) is { } folder)
        {
            Open(() => shell.OpenFolder(folder), "the report folder");
        }
    }

    [RelayCommand]
    private void ReportIssue() =>
        Open(() => shell.OpenUrl(IssueUrl.Build(_last?.Model, _last?.Firmware)), "the issue form");

    private bool CanStart() => !IsBusy;

    private bool CanStartWmi2() => !IsBusy && HasWmi2;

    private async Task MakeAsync(Func<ReportResult> make, bool askFirst)
    {
        if (IsBusy || (askFirst && !HasWmi2))
        {
            return; // the menu was built before the state changed
        }

        IsBusy = true;
        try
        {
            if (askFirst && !confirm.Ask(Strings.Report_Wmi2Confirm))
            {
                log.Info("Report with WMI2: not confirmed, nothing was read.");
                return;
            }

            notifier.ShowInfo(AppName, Strings.Report_Working);
            var result = await Task.Run(make);
            _last = result;
            OnPropertyChanged(nameof(LastReportPath));
            var saved = string.Format(Strings.Report_Saved, result.Path, Strings.Report_IssueMenu);
            notifier.ShowInfo(AppName, saved);
            status.ShowInfo($"{saved} {Strings.Cli_ReportGuidance}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error("The device report could not be created", ex);
            status.ShowWarning(Strings.Report_Failed);
            notifier.ShowError(AppName, Strings.Report_Failed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Open(Action open, string what)
    {
        try
        {
            open();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error($"Could not open {what}", ex);
            status.ShowWarning(Strings.Report_OpenFailed);
        }
    }
}
