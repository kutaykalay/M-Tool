using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.Core.Profiles;

/// <param name="SaveWarning">Set when the EC changed but settings.json could not be written.</param>
public sealed record CommandResult(WriteOutcome Outcome, string? SaveWarning = null);

/// <summary>
/// Runs the user's commands one at a time. A write that happened (or was validated in dry-run)
/// becomes the new <see cref="DesiredState"/> and is saved; anything else leaves it unchanged.
/// <see cref="ReapplyAsync"/> writes the saved state back after the EC reset it (reboot, resume).
/// This is the only writer of settings.json while the app runs: a later settings screen must go
/// through it, or it would overwrite that screen's changes with its own copy.
/// </summary>
public sealed class ProfileService
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly IP65Control _control;
    private readonly SettingsStore _store;
    private readonly IAppLog _log;

    // The built-in profiles plus the saved custom ones; a desired custom profile must resolve here.
    private readonly ProfileCatalog _catalog;
    private AppSettings _settings;
    private int _commands;

    /// <param name="catalog">The built-in profiles; the custom profiles in <paramref name="settings"/> are added.</param>
    /// <param name="settings">
    /// Already sanitized by the caller, which also keeps a copy of the file when profiles were dropped
    /// (<see cref="SettingsStore.PreserveCopy"/>); the warnings of the second pass here are not shown.
    /// </param>
    public ProfileService(IP65Control control, ProfileCatalog catalog, SettingsStore store, AppSettings settings, IAppLog log)
    {
        _control = control;
        _store = store;
        _log = log;

        // Sanitized here too, not only at load: this is the path from settings.json to EC writes.
        var sanitized = SettingsSanitizer.Sanitize(settings, catalog);
        _catalog = sanitized.Catalog;
        _settings = sanitized.Settings;
    }

    /// <summary>Raised on a thread-pool thread after the desired state changed (saved, or tried to be).</summary>
    public event Action<DesiredState>? DesiredChanged;

    public DesiredState Desired => Volatile.Read(ref _settings).Desired;

    /// <summary>True while a command is queued or writing; sensor polls missed meanwhile are expected.</summary>
    public bool IsBusy => Volatile.Read(ref _commands) > 0;

    public Task<CommandResult> SelectProfileAsync(string name)
    {
        if (_catalog.Find(name) is not { } profile)
        {
            return Task.FromResult(Rejected($"Bilinmeyen fan profili: {name}"));
        }

        return RunAsync(
            () => _control.ApplyFanProfileAsync(profile),
            desired => desired with { FanProfile = profile.Name });
    }

    public Task<CommandResult> SetPerformanceAsync(PerformanceMode mode) => RunAsync(
        () => _control.SetPerformanceAsync(mode),
        desired => desired with { Performance = mode });

    public Task<CommandResult> SetChargeLimitAsync(int percent)
    {
        if (percent is < EcWriteRules.MinChargeLimitPercent or > EcWriteRules.MaxChargeLimitPercent)
        {
            return Task.FromResult(Rejected(
                $"Şarj limiti %{EcWriteRules.MinChargeLimitPercent}-{EcWriteRules.MaxChargeLimitPercent} olmalı (%{percent})."));
        }

        return RunAsync(
            () => _control.SetChargeLimitAsync(percent),
            desired => desired with { ChargeLimitPercent = percent });
    }

    /// <summary>Temporary switch: never part of the desired state.</summary>
    public Task<CommandResult> SetCoolerBoostAsync(bool on) => RunAsync(() => _control.SetCoolerBoostAsync(on), update: null);

    /// <param name="portUse">
    /// <see cref="PortUse.None"/> for automatic reapplying (start-up, resume): the charge limit is
    /// left out, so the raw port is never touched. The desired state itself stays as it is.
    /// </param>
    public async Task<IReadOnlyList<WriteOutcome>> ReapplyAsync(PortUse portUse)
    {
        Interlocked.Increment(ref _commands);
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var desired = portUse == PortUse.Allowed ? Desired : Desired.WithoutPortParts();
            return await _control.ApplyDesiredAsync(desired, _catalog).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
            Interlocked.Decrement(ref _commands);
        }
    }

    private async Task<CommandResult> RunAsync(Func<Task<WriteOutcome>> write, Func<DesiredState, DesiredState>? update)
    {
        var (result, changed) = await RunLockedAsync(write, update).ConfigureAwait(false);
        if (changed is not null)
        {
            RaiseDesiredChanged(changed);
        }

        return result;
    }

    private async Task<(CommandResult Result, DesiredState? Changed)> RunLockedAsync(
        Func<Task<WriteOutcome>> write, Func<DesiredState, DesiredState>? update)
    {
        Interlocked.Increment(ref _commands);
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var outcome = await write().ConfigureAwait(false);
            if (update is null || outcome.Status is not (WriteStatus.Applied or WriteStatus.DryRun))
            {
                return (new CommandResult(outcome), null);
            }

            var saveWarning = Remember(update(Desired));
            return (new CommandResult(outcome, saveWarning), Desired);
        }
        finally
        {
            _oneAtATime.Release();
            Interlocked.Decrement(ref _commands);
        }
    }

    private void RaiseDesiredChanged(DesiredState desired)
    {
        try
        {
            DesiredChanged?.Invoke(desired);
        }
        catch (Exception ex)
        {
            // The EC was written and the choice saved; a subscriber's bug must not hide that.
            _log.Error("İstenen durum abonesi hata verdi", ex);
        }
    }

    private string? Remember(DesiredState desired)
    {
        var updated = Volatile.Read(ref _settings) with { Desired = desired };
        Volatile.Write(ref _settings, updated);
        try
        {
            _store.Save(updated);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("settings.json yazılamadı", ex);
            return $"Ayar kaydedilemedi (settings.json: {ex.Message}); yeniden başlatınca eski seçim geri gelir.";
        }
    }

    private static CommandResult Rejected(string message) => new(new WriteOutcome(WriteStatus.Rejected, [], message));
}
