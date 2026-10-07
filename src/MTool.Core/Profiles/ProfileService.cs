using System.Text.Json;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Settings;

namespace MTool.Core.Profiles;

/// <param name="SaveWarning">Set when the EC changed but settings.json could not be written.</param>
public sealed record CommandResult(WriteOutcome Outcome, string? SaveWarning = null);

/// <summary>The result of a change to the custom profiles; these never write the EC.</summary>
/// <param name="Error">Why nothing changed (Turkish, for the user); null on success.</param>
/// <param name="SaveWarning">Set when the change is kept in memory but settings.json could not be written.</param>
public sealed record ProfileCommandResult(string? Error, string? SaveWarning = null);

/// <summary>
/// Runs the user's commands one at a time. A write that happened (or was validated in dry-run)
/// becomes the new <see cref="DesiredState"/> and is saved; anything else leaves it unchanged.
/// <see cref="ReapplyAsync"/> writes the saved state back after the EC reset it (reboot, resume).
/// Changes to the custom profiles take the same turn, so a write never sees half of one.
/// This is the only writer of settings.json while the app runs: a later settings screen must go
/// through it, or it would overwrite that screen's changes with its own copy.
/// </summary>
public sealed class ProfileService
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly IP65Control _control;
    private readonly SettingsStore _store;
    private readonly IAppLog _log;

    // Settings and the catalog built from them, replaced together so a reader never mixes two versions.
    private Snapshot _current;
    private int _commands;

    // Where the laptop draws power from as last told; read and written only inside the turn. Null until
    // the first SwitchPowerSourceAsync: a choice by hand before that updates no pair.
    private PowerSource? _source;

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
        _current = new Snapshot(sanitized.Settings, sanitized.Catalog);
    }

    /// <summary>Raised on any thread (the caller's or a thread-pool one; subscribers marshal to the UI) after the desired state changed (saved, or tried to be).</summary>
    /// <remarks>
    /// Raised after the turn ends, so two changes close together can arrive out of order: the payload
    /// may already be stale. A subscriber that keeps state reads <see cref="Desired"/> instead.
    /// </remarks>
    public event Action<DesiredState>? DesiredChanged;

    /// <summary>Raised on any thread (the caller's or a thread-pool one; subscribers marshal to the UI) after a custom profile was added, renamed, deleted or saved; read <see cref="Catalog"/>.</summary>
    public event Action? CatalogChanged;

    public DesiredState Desired => Current.Settings.Desired;

    /// <summary>The built-in and custom profiles; a new instance after every change, never changed in place.</summary>
    public ProfileCatalog Catalog => Current.Catalog;

    /// <summary>
    /// Both from the same version: reading <see cref="Desired"/> and <see cref="Catalog"/> one after the
    /// other can straddle a rename, and the desired name would then be missing from the catalog.
    /// </summary>
    public (DesiredState Desired, ProfileCatalog Catalog) DesiredWithCatalog
    {
        get
        {
            var current = Current;
            return (current.Settings.Desired, current.Catalog);
        }
    }

    /// <summary>True while a command is queued or writing; sensor polls missed meanwhile are expected.</summary>
    public bool IsBusy => Volatile.Read(ref _commands) > 0;

    private Snapshot Current => Volatile.Read(ref _current);

    public Task<CommandResult> SelectProfileAsync(string name)
    {
        // Looked up inside the turn: a rename or delete queued before it must not leave an unknown name in the desired state.
        FanProfile? profile = null;
        return RunAsync(
            () => (profile = Catalog.Find(name)) is null
                ? Task.FromResult(RejectedOutcome($"Bilinmeyen fan profili: {ProfileNameRules.Printable(name)}"))
                : _control.ApplyFanProfileAsync(profile),
            desired => desired with { FanProfile = profile!.Name });
    }

    public Task<CommandResult> SetPerformanceAsync(PerformanceMode mode) => RunAsync(
        () => _control.SetPerformanceAsync(mode),
        desired => desired with { Performance = mode });

    public Task<CommandResult> SetChargeLimitAsync(int percent)
    {
        if (percent is < EcWriteRules.MinChargeLimitPercent or > EcWriteRules.MaxChargeLimitPercent)
        {
            return Task.FromResult(new CommandResult(RejectedOutcome(
                $"Şarj limiti %{EcWriteRules.MinChargeLimitPercent}-{EcWriteRules.MaxChargeLimitPercent} olmalı (%{percent}).")));
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
            var current = Current;
            var desired = portUse == PortUse.Allowed ? current.Settings.Desired : current.Settings.Desired.WithoutPortParts();
            return await _control.ApplyDesiredAsync(desired, current.Catalog).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
            Interlocked.Decrement(ref _commands);
        }
    }

    public PowerSwitchSettings PowerSwitch => Current.Settings.PowerSwitch;

    /// <summary>
    /// The laptop now runs on <paramref name="source"/> (also at start-up and on wake). While switching
    /// is on, the desired state takes that source's pair and is saved. Only the WMI parts are written,
    /// and only when <paramref name="write"/> and the desired state changed. Null when nothing was
    /// written: switching off, the same source as before, no pair yet, equal pairs, or no write asked.
    /// </summary>
    /// <param name="write">False while the EC access gate is shut; the next reapply writes the new state.</param>
    public async Task<IReadOnlyList<WriteOutcome>?> SwitchPowerSourceAsync(PowerSource source, bool write)
    {
        // Counted in IsBusy only when it may write: polls and shutdown wait for EC traffic, not file saves.
        if (write)
        {
            Interlocked.Increment(ref _commands);
        }

        DesiredState? changed = null;
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var before = Current.Settings;
            var sourceBefore = _source;
            _source = source;
            if (sourceBefore == source)
            {
                return null;
            }

            var aligned = PowerSwitchRules.Align(before, source);
            if (aligned == before)
            {
                return null;
            }

            Remember(aligned);
            if (aligned.Desired == before.Desired)
            {
                return null;
            }

            changed = aligned.Desired;
            _log.Info($"Power source switch ({source}): {ProfileNameRules.Printable(aligned.Desired.FanProfile)} + {aligned.Desired.Performance?.ToString() ?? "-"} (write: {(write ? "yes" : "no, gate closed")})");
            return write
                ? await _control.ApplyDesiredAsync(aligned.Desired.WithoutPortParts(), Current.Catalog).ConfigureAwait(false)
                : null;
        }
        finally
        {
            _oneAtATime.Release();
            if (write)
            {
                Interlocked.Decrement(ref _commands);
            }

            if (changed is not null)
            {
                RaiseDesiredChanged(changed);
            }
        }
    }

    /// <summary>Turning it on remembers what is set now for the current source; neither way writes the EC.</summary>
    public async Task<ProfileCommandResult> SetPowerSwitchAsync(bool enabled)
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            return new ProfileCommandResult(null, Remember(PowerSwitchRules.Enable(Current.Settings, enabled, _source)));
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public Task<ProfileCommandResult> AddProfileAsync(string name, FanCurves curves) =>
        ChangeLibraryAsync(settings => ProfileLibrary.Add(settings, name, curves));

    /// <summary>When it is the desired profile, the desired state follows the new name; the EC is not written.</summary>
    public Task<ProfileCommandResult> RenameProfileAsync(string oldName, string newName) =>
        ChangeLibraryAsync(settings => ProfileLibrary.Rename(settings, oldName, newName));

    /// <summary>The desired profile cannot be deleted; pick another one first.</summary>
    public Task<ProfileCommandResult> DeleteProfileAsync(string name) =>
        ChangeLibraryAsync(settings => ProfileLibrary.Delete(settings, name));

    /// <summary>Saving does not write the EC, even for the desired profile; reapplying does.</summary>
    public Task<ProfileCommandResult> SaveCurvesAsync(string name, FanCurves curves) =>
        ChangeLibraryAsync(settings => ProfileLibrary.SaveCurves(settings, name, curves));

    private async Task<ProfileCommandResult> ChangeLibraryAsync(Func<AppSettings, LibraryResult> change)
    {
        // Not counted in IsBusy: nothing here talks to the EC.
        DesiredState desiredBefore;
        DesiredState desiredAfter;
        string? saveWarning;
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var before = Current.Settings;
            var result = change(before);
            if (result.Updated is not { } updated)
            {
                return new ProfileCommandResult(result.Error ?? "Profil değiştirilemedi.");
            }

            saveWarning = Remember(updated);
            desiredBefore = before.Desired;
            desiredAfter = updated.Desired;
        }
        finally
        {
            _oneAtATime.Release();
        }

        Raise(CatalogChanged, "Profile list subscriber failed");
        if (desiredAfter != desiredBefore)
        {
            RaiseDesiredChanged(desiredAfter);
        }

        return new ProfileCommandResult(null, saveWarning);
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

            var saveWarning = Remember(WithChoice(Current.Settings, update(Desired)));
            return (new CommandResult(outcome, saveWarning), Desired);
        }
        finally
        {
            _oneAtATime.Release();
            Interlocked.Decrement(ref _commands);
        }
    }

    /// <summary>
    /// A new fan profile or performance mode chosen by hand also becomes the pair of the current power
    /// source; the charge limit is not part of a pair, so changing it leaves the pairs alone.
    /// </summary>
    private AppSettings WithChoice(AppSettings settings, DesiredState desired)
    {
        var updated = settings with { Desired = desired };
        var pairPartsChanged = desired.FanProfile != settings.Desired.FanProfile || desired.Performance != settings.Desired.Performance;
        return pairPartsChanged && _source is { } source ? PowerSwitchRules.Record(updated, source) : updated;
    }

    // The EC was written or the profiles changed; a subscriber's bug must not hide that.
    private void RaiseDesiredChanged(DesiredState desired) =>
        Raise(() => DesiredChanged?.Invoke(desired), "Desired state subscriber failed");

    private void Raise(Action? handler, string failure)
    {
        try
        {
            handler?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(failure, ex);
        }
    }

    /// <summary>Called inside the turn. The change is kept in memory even when the file cannot be written.</summary>
    private string? Remember(AppSettings updated)
    {
        Volatile.Write(ref _current, new Snapshot(updated, Current.Catalog.WithCustom(updated.CustomProfiles)));
        try
        {
            _store.Save(updated);
            return null;
        }
        // Every way the save can fail; an escaping exception would skip the events after the state changed.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or System.Security.SecurityException or JsonException)
        {
            _log.Error("Could not write settings.json", ex);
            return $"Ayar kaydedilemedi (settings.json: {ex.Message}); yeniden başlatınca eski hali geri gelir.";
        }
    }

    private static WriteOutcome RejectedOutcome(string message) => new(WriteStatus.Rejected, [], message);

    private sealed record Snapshot(AppSettings Settings, ProfileCatalog Catalog);
}
