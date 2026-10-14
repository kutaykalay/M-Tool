using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Diagnostics;

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
/// "Send EC data" collects the same read-only parts (no DSDT), shows the full text, and sends it only
/// when the user chooses Send; the text sent is the text shown. Its outcome is told in the user's
/// language and logged in English, never with the text itself.
/// </summary>
/// <param name="stop">Cancelled when M-Tool closes: a send still running is dropped without a message.</param>
internal sealed partial class ReportViewModel(
    ReportService service,
    IConfirm confirm,
    ISubmissionPreview preview,
    SubmissionClient submissions,
    IShellLauncher shell,
    StatusViewModel status,
    INotifier notifier,
    IAppLog log,
    TimeProvider? time = null,
    CancellationToken stop = default) : ObservableObject
{
    /// <summary>Far above a normal report (seconds), so only a hung WMI or EC call is cut off.</summary>
    public static readonly TimeSpan ReportTimeout = TimeSpan.FromMinutes(3);

    private const string AppName = "M-Tool";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private ReportResult? _last;

    /// <summary>True while a report is being made or sent, or one of its questions is open.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendEcDataCommand))]
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

    /// <summary>
    /// Asks before reading WMI2 (only on a WMI2 laptop; no is the default and then nothing is read or
    /// sent), collects off the UI thread, refuses a text over <see cref="EcSubmission.MaxBytes"/>, shows
    /// the text and sends it once if the user chooses Send.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task SendEcDataAsync()
    {
        if (IsBusy)
        {
            return; // the menu was built before the state changed
        }

        IsBusy = true;
        try
        {
            var withWmi2 = HasWmi2;
            if (withWmi2 && !confirm.Ask(Strings.Submit_Wmi2Confirm, defaultYes: false))
            {
                log.Info("EC submission: WMI2 not confirmed, nothing was read or sent.");
                return;
            }

            notifier.ShowInfo(AppName, Strings.Submit_Collecting);
            var json = await OffUiAsync(() => service.CollectForSubmission(withWmi2), "Collecting the EC data");
            if (json is null)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes > EcSubmission.MaxBytes)
            {
                log.Warn($"EC submission: payload {bytes} bytes > {EcSubmission.MaxBytes}, not sent.");
                Warn(Strings.Submit_TooLarge);
                return;
            }

            if (!preview.Confirm(json))
            {
                log.Info("EC submission: not sent (declined in preview).");
                return;
            }

            Tell(await submissions.SendAsync(json, stop), bytes);
        }
        finally
        {
            IsBusy = false;
        }
    }

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
        try
        {
            // No is the default answer: Enter on this question must not start the WMI2 report.
            if (askFirst && !confirm.Ask(Strings.Report_Wmi2Confirm, defaultYes: false))
            {
                log.Info("Report with WMI2: not confirmed, nothing was read.");
                return null;
            }

            notifier.ShowInfo(AppName, Strings.Report_Working);
            var result = await OffUiAsync(make, "The device report");
            if (result is null)
            {
                return null;
            }

            _last = result;
            OnPropertyChanged(nameof(LastReportPath));
            var shownPath = Path.Combine(AppPaths.ReportsShown, Path.GetFileName(result.Path));
            var saved = string.Format(Strings.Report_Saved, shownPath, Strings.Report_IssueMenu);
            notifier.ShowInfo(AppName, saved);
            status.ShowInfo($"{saved} {Strings.Cli_ReportGuidance}");
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs <paramref name="make"/> off the UI thread; null when it failed or timed out (the user has been told).</summary>
    /// <param name="what">The work, for the log: "The device report".</param>
    private async Task<T?> OffUiAsync<T>(Func<T> make, string what)
        where T : class
    {
        Task<T>? work = null;
        try
        {
            work = Task.Run(make);
            return await work.WaitAsync(ReportTimeout, _time);
        }
        catch (TimeoutException ex) when (work is { IsCompleted: false })
        {
            // The work cannot be stopped: it runs on and its outcome is dropped (a fault is only logged).
            _ = work.ContinueWith(
                late => log.Warn($"{what} that was given up on failed later: {late.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            log.Error($"{what} did not finish within {ReportTimeout.TotalMinutes:0.#} minutes", ex);
            Warn(Strings.Report_TimedOut);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error($"{what} failed", ex);
            Warn(Strings.Report_Failed);
            return null;
        }
    }

    /// <summary>Tells how a send ended; the log has the code, never the text that was sent.</summary>
    private void Tell(SubmissionOutcome outcome, int bytes)
    {
        var code = $"HTTP {outcome.HttpStatus} {outcome.ErrorCode ?? "-"}";
        switch (outcome.Status)
        {
            case SubmissionStatus.Created:
                log.Info($"EC submission: sent ({bytes} bytes), id {outcome.Id ?? "none"}.");
                Thank(Strings.Submit_Sent, outcome.Id);
                break;
            case SubmissionStatus.Duplicate:
                log.Info($"EC submission: duplicate, id {outcome.Id ?? "none"}.");
                Thank(Strings.Submit_Duplicate, outcome.Id);
                break;
            case SubmissionStatus.RateLimited:
                var wait = outcome.RetryAfter ?? SubmissionOutcome.DefaultRetryAfter;
                log.Warn($"EC submission refused: 429, retry after {wait.TotalSeconds:0} s.");
                Warn(string.Format(Strings.Submit_RateLimited, (int)Math.Ceiling(wait.TotalMinutes)));
                break;
            case SubmissionStatus.Rejected:
                log.Warn($"EC submission rejected: {code}.");
                Warn(Strings.Submit_Rejected);
                break;
            case SubmissionStatus.Unavailable:
                log.Warn($"EC submission failed: {code}.");
                Warn(Strings.Submit_Unavailable);
                break;
            case SubmissionStatus.Network:
                log.Warn(outcome.HttpStatus is { } proxy ? $"EC submission failed: network (HTTP {proxy})." : "EC submission failed: network.");
                Warn(Strings.Submit_Network);
                break;
            case SubmissionStatus.Timeout:
                log.Warn("EC submission failed: timeout.");
                Warn(Strings.Submit_Timeout);
                break;
            default:
                log.Info("EC submission: cancelled at shutdown.");
                break;
        }
    }

    private void Thank(string first, string? id)
    {
        var message = id is null ? first : $"{first} {string.Format(Strings.Submit_Record, id)}";
        notifier.ShowInfo(AppName, message);
        status.ShowInfo(message);
    }

    private void Warn(string message)
    {
        status.ShowWarning(message);
        notifier.ShowError(AppName, message);
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
