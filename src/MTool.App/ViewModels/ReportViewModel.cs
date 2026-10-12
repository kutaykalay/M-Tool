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
/// Messages shown to the user never hold a path from an error or a user name: the report is named by its
/// file name and <see cref="AppPaths.ReportsShown"/> (%AppData%), and the log has the full path and the details.
/// A call that hangs (WMI can) is given up on after <see cref="ReportTimeout"/>: the commands work again
/// and whatever the abandoned work finishes later is ignored.
/// </summary>
internal sealed partial class ReportViewModel(
    ReportService service, IConfirm confirm, IShellLauncher shell, StatusViewModel status, INotifier notifier, IAppLog log, TimeProvider? time = null) : ObservableObject
{
    /// <summary>Far above a normal report (seconds), so only a hung WMI or EC call is cut off.</summary>
    public static readonly TimeSpan ReportTimeout = TimeSpan.FromMinutes(3);

    private const string AppName = "M-Tool";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private ReportResult? _last;

    /// <summary>True while a report is being made or the WMI2 question is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateWithWmi2Command))]
    [NotifyCanExecuteChangedFor(nameof(ReportIssueCommand))]
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
    private async Task CreateAsync() => await MakeAsync(service.Create, askFirst: false);

    [RelayCommand(CanExecute = nameof(CanStartWmi2))]
    private async Task CreateWithWmi2Async() => await MakeAsync(service.CreateWithWmi2, askFirst: true);

    [RelayCommand]
    private void OpenFolder()
    {
        if (LastReportPath is { } path && Path.GetDirectoryName(path) is { } folder)
        {
            Open(() => shell.OpenFolder(folder), "the report folder");
        }
    }

    /// <summary>
    /// Opens the prefilled issue form. With no report made since start-up it first makes the plain report
    /// (never WMI2, no question) the same way <see cref="CreateCommand"/> does, so the form has the model
    /// and firmware; if that fails or times out the form stays closed and the usual warning is shown.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ReportIssueAsync()
    {
        var report = _last ?? await MakeAsync(service.Create, askFirst: false);
        if (report is null)
        {
            return;
        }

        Open(() => shell.OpenUrl(IssueUrl.Build(report.Model, report.Firmware)), "the issue form");
    }

    private bool CanStart() => !IsBusy;

    private bool CanStartWmi2() => !IsBusy && HasWmi2;

    /// <summary>Makes a report; null when it was refused, not confirmed, failed or timed out (the user has been told).</summary>
    private async Task<ReportResult?> MakeAsync(Func<ReportResult> make, bool askFirst)
    {
        if (IsBusy || (askFirst && !HasWmi2))
        {
            return null; // the menu was built before the state changed
        }

        IsBusy = true;
        Task<ReportResult>? work = null;
        try
        {
            // No is the default answer: Enter on this question must not start the WMI2 report.
            if (askFirst && !confirm.Ask(Strings.Report_Wmi2Confirm, defaultYes: false))
            {
                log.Info("Report with WMI2: not confirmed, nothing was read.");
                return null;
            }

            notifier.ShowInfo(AppName, Strings.Report_Working);
            work = Task.Run(make);
            var result = await work.WaitAsync(ReportTimeout, _time);
            _last = result;
            OnPropertyChanged(nameof(LastReportPath));
            var shownPath = Path.Combine(AppPaths.ReportsShown, Path.GetFileName(result.Path));
            var saved = string.Format(Strings.Report_Saved, shownPath, Strings.Report_IssueMenu);
            notifier.ShowInfo(AppName, saved);
            status.ShowInfo($"{saved} {Strings.Cli_ReportGuidance}");
            return result;
        }
        catch (TimeoutException ex) when (work is { IsCompleted: false })
        {
            // The work cannot be stopped: it runs on and its outcome is dropped (a fault is only logged).
            _ = work.ContinueWith(
                late => log.Warn($"The device report that was given up on failed later: {late.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            log.Error($"The device report did not finish within {ReportTimeout.TotalMinutes:0.#} minutes", ex);
            status.ShowWarning(Strings.Report_TimedOut);
            notifier.ShowError(AppName, Strings.Report_TimedOut);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error("The device report could not be created", ex);
            status.ShowWarning(Strings.Report_Failed);
            notifier.ShowError(AppName, Strings.Report_Failed);
            return null;
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
