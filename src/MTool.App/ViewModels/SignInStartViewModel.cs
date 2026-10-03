using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTool.App.Services;
using MTool.Core;

namespace MTool.App.ViewModels;

/// <summary>
/// The tray's "start at sign-in" switch. A task that starts another exe, or a task by this name
/// that M-Tool did not make, is reported and never repaired silently: the task runs elevated, so it
/// changes only when the user switches it off and on again. The exe may live anywhere: switching on
/// registers it where it is now. Task Scheduler calls run off the UI thread; until the first query
/// answers, the state is unknown and the switch is disabled.
/// </summary>
public sealed partial class SignInStartViewModel(
    IStartupTask task, string exePath, StatusViewModel status, IAppLog log) : ObservableObject
{
    private string? _repairWarning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    public partial bool IsKnown { get; private set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; private set; }

    public async Task LoadAsync()
    {
        if (await QueryAsync() is not { } registered || !StartupTaskSpec.NeedsRepair(registered, exePath))
        {
            return;
        }

        var found = registered.Length == 0 ? "M-Tool'un tanımadığı bir görev" : $"başka bir exe: {registered}";
        _repairWarning = $"Oturum açılışında başlatma beklenen görev değil ({found}). " +
            "Bu exe'ye almak için tepsi menüsünden kapatıp yeniden açın.";
        log.Warn(_repairWarning);
        status.AddWarning(_repairWarning);
    }

    [RelayCommand(CanExecute = nameof(IsKnown))]
    private async Task ToggleAsync()
    {
        var enable = !IsEnabled;
        try
        {
            await Task.Run(() =>
            {
                if (enable)
                {
                    task.Enable(exePath);
                }
                else
                {
                    task.Disable();
                }
            });
            log.Info(enable ? $"Oturum açılışında başlatma açıldı: {exePath}" : "Oturum açılışında başlatma kapatıldı.");
            ClearRepairWarning();
        }
        catch (Exception ex)
        {
            log.Error("Oturum açılışında başlatma değiştirilemedi", ex);
            status.ShowWarning($"Oturum açılışında başlatma değiştirilemedi: {ex.Message}");
        }

        await QueryAsync();
    }

    /// <summary>
    /// Updates <see cref="IsKnown"/> and <see cref="IsEnabled"/>; returns the registered exe, or null
    /// when there is no task or it could not be read.
    /// </summary>
    private async Task<string?> QueryAsync()
    {
        try
        {
            var registered = await Task.Run(task.QueryRegisteredExe);
            (IsKnown, IsEnabled) = (true, registered is not null);
            return registered;
        }
        catch (Exception ex)
        {
            log.Warn($"Oturum açılışı görevi okunamadı: {ex.Message}");
            IsKnown = false;
            return null;
        }
    }

    private void ClearRepairWarning()
    {
        if (_repairWarning is { } text)
        {
            status.ClearMessage(ifShowing: text);
            _repairWarning = null;
        }
    }
}
