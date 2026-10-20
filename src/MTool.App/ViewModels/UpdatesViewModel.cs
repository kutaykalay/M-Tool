using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Reports;
using MTool.App.Resources;
using MTool.App.Updates;
using MTool.Core;
using MTool.Core.Profiles;
using MTool.Core.Updates;

namespace MTool.App.ViewModels;

/// <summary>
/// The update part of the About tab (Aşama 14b). It only tells: a newer release is named and its page
/// opened in the browser on request; M-Tool never downloads or replaces itself. GitHub is asked only by
/// the button, or once a minute after start-up when the user turned that on (off by default). After
/// each check the button rests for <see cref="Rest"/>, longer when GitHub asks for it.
/// </summary>
/// <param name="client">Null when this build's version is unknown: nothing can be compared then.</param>
/// <param name="stop">Cancelled when M-Tool closes.</param>
internal sealed partial class UpdatesViewModel(
    IUpdateClient? client,
    ProfileService service,
    StatusViewModel status,
    INotifier notifier,
    IShellLauncher shell,
    IUiDispatcher ui,
    IAppLog log,
    TimeProvider time,
    CancellationToken stop) : ObservableObject, IDisposable
{
    public static readonly TimeSpan Rest = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private const string AppName = "M-Tool";

    private DateTimeOffset _readyAt = DateTimeOffset.MinValue;
    private ITimer? _restTimer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    public partial string? StatusText { get; private set; } = client is null ? Strings.Update_UnknownVersion : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenReleasePageCommand))]
    public partial SemVersion? NewVersion { get; private set; }

    public bool AutoCheck => service.AutoUpdateCheck;

    /// <summary>Called once at start-up; returns at once unless the user turned the automatic check on.</summary>
    public async Task RunStartupCheckAsync()
    {
        if (client is null || !service.AutoUpdateCheck)
        {
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, time, stop);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Turned off during the wait, or the user is already checking by hand.
        if (!service.AutoUpdateCheck || IsChecking)
        {
            return;
        }

        if (await RunAsync() is { Status: UpdateStatus.UpdateAvailable, Latest: { } latest })
        {
            notifier.ShowInfo(AppName, string.Format(Strings.Update_AvailableBalloon, latest));
        }
    }

    public void Dispose() => _restTimer?.Dispose();

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync() => await RunAsync();

    [RelayCommand(CanExecute = nameof(HasNewVersion))]
    private void OpenReleasePage()
    {
        if (NewVersion is not { } version)
        {
            return;
        }

        try
        {
            shell.OpenUrl(ProjectLinks.ReleaseTag(version));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error("Could not open the release page", ex);
            status.ShowWarning(Strings.Report_OpenFailed);
        }
    }

    /// <summary>Only changes the settings file; turning it on is not a check.</summary>
    [RelayCommand]
    private async Task ToggleAutoCheckAsync()
    {
        var result = await service.SetAutoUpdateCheckAsync(!service.AutoUpdateCheck);

        // A clicked check box shows the new state by itself; when nothing changed it must hear the old one again.
        OnPropertyChanged(nameof(AutoCheck));
        if ((result.Error ?? result.SaveWarning) is { } problem)
        {
            status.ShowWarning(problem);
        }
    }

    private bool CanCheck() => client is not null && !IsChecking && time.GetUtcNow() >= _readyAt;

    private bool HasNewVersion() => NewVersion is not null;

    private async Task<UpdateCheckOutcome?> RunAsync()
    {
        if (client is null)
        {
            return null;
        }

        IsChecking = true;
        StatusText = Strings.Update_Checking;
        var outcome = await client.CheckAsync(stop);
        IsChecking = false;

        // Only the outcome's own fields: nothing of GitHub's answer reaches the log.
        log.Info($"Update check: {outcome.Status}, HTTP {outcome.HttpStatus?.ToString() ?? "-"}, latest {outcome.Latest?.ToString() ?? "-"}");
        StatusText = Text(outcome);
        NewVersion = outcome is { Status: UpdateStatus.UpdateAvailable, Latest: { } latest } ? latest : null;
        if (outcome.Status != UpdateStatus.Cancelled)
        {
            RestFor(outcome.RetryAfter is { } wait && wait > Rest ? wait : Rest);
        }

        return outcome;
    }

    private void RestFor(TimeSpan wait)
    {
        _readyAt = time.GetUtcNow() + wait;
        CheckCommand.NotifyCanExecuteChanged();
        _restTimer?.Dispose();
        _restTimer = time.CreateTimer(_ => ui.Post(CheckCommand.NotifyCanExecuteChanged), null, wait, Timeout.InfiniteTimeSpan);
    }

    private static string? Text(UpdateCheckOutcome outcome) => outcome.Status switch
    {
        UpdateStatus.UpToDate => Strings.Update_UpToDate,
        UpdateStatus.UpdateAvailable => string.Format(Strings.Update_Available, outcome.Latest),
        UpdateStatus.CurrentIsNewer => Strings.Update_CurrentIsNewer,
        UpdateStatus.RateLimited => string.Format(
            Strings.Update_RateLimited, (int)Math.Ceiling((outcome.RetryAfter ?? UpdateCheckOutcome.DefaultRetryAfter).TotalMinutes)),
        UpdateStatus.Network => Strings.Update_Unreachable,
        UpdateStatus.Timeout => Strings.Update_Timeout,
        UpdateStatus.Cancelled => null,
        _ => Strings.Update_Unreadable,
    };
}
