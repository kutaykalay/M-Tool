using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary>
/// <see cref="IP65Control"/> over the EC worker (reads) and the gateway (writes). Cooler Boost and
/// the charge limit sit behind the raw port, which races Windows' EC driver, so they are read once
/// and then kept in a cache that port writes update: refreshing the window never uses the port.
/// A Cooler Boost write and a reapplied charge limit read them fresh first, so a cached value is
/// never the base of a write. After a failed port write they stay unknown: the gateway is locked
/// then, and reading the port again would only add risk. Changes made outside M-Tool (an Fn key,
/// a resume) are not seen until the next fresh read.
/// </summary>
public sealed class P65Control(EcWorker worker, WriteAccessSetup setup, IAppLog log, EcAccessRetry? retry = null) : IP65Control
{
    private readonly EcAccessRetry _retry = retry ?? EcAccessRetry.Default;

    // Every port read and cache update holds this, so an older read can never overwrite a newer write.
    private readonly SemaphoreSlim _portGate = new(1, 1);
    private PortState? _port;
    private bool _portSettled; // read, or deliberately unknown after a failed write: no automatic read

    private bool PortAvailable => setup.Gateway.IsPortAvailable;

    public DeviceAccess Access => setup.Gateway.LockReason is { } reason
        ? new DeviceAccess(setup.Firmware, WriteMode.Locked, reason, PortAvailable)
        : new DeviceAccess(setup.Firmware, setup.Gateway.IsDryRun ? WriteMode.DryRun : WriteMode.Enabled, null, PortAvailable);

    public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken = default) =>
        worker.RunAsync(ec => new P65Device(ec).ReadSensors(), cancellationToken);

    public async Task<ControlState> ReadControlStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await worker.RunRetryingAsync(ec => new P65Device(ec).ReadControlState(), _retry, log.Warn, cancellationToken)
            .ConfigureAwait(false);
        return state with { Port = await CachedPortStateAsync(cancellationToken).ConfigureAwait(false) };
    }

    public Task<WriteOutcome> ApplyFanProfileAsync(FanProfile profile, CancellationToken cancellationToken = default) =>
        setup.Gateway.ApplyAsync(WritePlans.FanCurves(profile.Curves, profile.Name), cancellationToken);

    /// <summary>The gateway checks again under the EC lock that only bit 7 changes.</summary>
    public async Task<WriteOutcome> SetCoolerBoostAsync(bool on, CancellationToken cancellationToken = default)
    {
        if (PortRefusal("Cooler Boost") is { } refused)
        {
            return refused;
        }

        await _portGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (current, error) = await FreshReadLockedAsync("Cooler Boost", cancellationToken).ConfigureAwait(false);
            return current is null
                ? new WriteOutcome(WriteStatus.Rejected, [], $"EC erişilemedi: {error}")
                : await ApplyPortLockedAsync(WritePlans.CoolerBoost(on, current.CoolerBoostRaw), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _portGate.Release();
        }
    }

    public Task<WriteOutcome> SetPerformanceAsync(PerformanceMode mode, CancellationToken cancellationToken = default) =>
        setup.Gateway.ApplyAsync(WritePlans.Performance(mode), cancellationToken);

    /// <summary>Without a port the gateway refuses the plan before any EC access.</summary>
    public async Task<WriteOutcome> SetChargeLimitAsync(int percent, CancellationToken cancellationToken = default)
    {
        await _portGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ApplyPortLockedAsync(WritePlans.ChargeLimit(percent), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _portGate.Release();
        }
    }

    public Task<WriteOutcome> SetFanModeAsync(FanMode mode, CancellationToken cancellationToken = default) =>
        setup.Gateway.ApplyAsync(WritePlans.Fan(mode), cancellationToken);

    /// <remarks>
    /// A charge limit the EC already holds adds no outcome. The fan table is always part of the
    /// desired state, so the list is never empty.
    /// </remarks>
    public async Task<IReadOnlyList<WriteOutcome>> ApplyDesiredAsync(
        DesiredState desired, ProfileCatalog catalog, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<WriteOutcome>();
        foreach (var plan in desired.ToPlans(catalog))
        {
            if (await ApplyDesiredPartAsync(plan, cancellationToken).ConfigureAwait(false) is not { } outcome)
            {
                continue;
            }

            outcomes.Add(outcome);
            // Anything but a done write stops here: a later fan mode switch must not run on a table
            // that was not the one asked for.
            if (outcome.Status is not (WriteStatus.Applied or WriteStatus.DryRun))
            {
                break;
            }
        }

        return outcomes.AsReadOnly();
    }

    /// <summary>
    /// Null when the EC already holds the part. The charge limit is read fresh, not from the cache:
    /// a reboot or resume may have reset it, and an unchanged value is not worth a port write.
    /// </summary>
    private async Task<WriteOutcome?> ApplyDesiredPartAsync(WritePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Writes is not [{ Register: EcMap.ChargeLimit } write] || PortRefusal(plan.Description) is not null)
        {
            // The gateway refuses a port plan it cannot write, with its own reason, before any EC access.
            return await setup.Gateway.ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        await _portGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (current, error) = await FreshReadLockedAsync(plan.Description, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return new WriteOutcome(WriteStatus.Rejected, plan.Writes, $"EC erişilemedi: {error}");
            }

            if (current.ChargeLimitRaw == write.Value)
            {
                log.Info($"{plan.Description}: EC zaten bu değerde, yazılmadı.");
                return null;
            }

            return await ApplyPortLockedAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _portGate.Release();
        }
    }

    /// <summary>A refusal that needs no EC access (no port, writes locked), or null.</summary>
    private WriteOutcome? PortRefusal(string what) =>
        !PortAvailable ? new WriteOutcome(WriteStatus.Rejected, [], $"{what} kullanılamaz: port kapalı (yalnızca WMI).")
        : setup.Gateway.LockReason is { } reason ? new WriteOutcome(WriteStatus.Rejected, [], $"EC yazma kapalı: {reason}")
        : null;

    /// <summary>Call holding <see cref="_portGate"/>. A done write updates the cache, a failed one makes it unknown.</summary>
    private async Task<WriteOutcome> ApplyPortLockedAsync(WritePlan plan, CancellationToken cancellationToken)
    {
        var outcome = await setup.Gateway.ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
        switch (outcome.Status)
        {
            case WriteStatus.Applied when _port is { } cached && plan.Writes is [var write]:
                _port = write.Register == EcMap.CoolerBoost
                    ? cached with { CoolerBoostRaw = write.Value }
                    : cached with { ChargeLimitRaw = write.Value };
                break;
            case WriteStatus.FailedRecovered or WriteStatus.FailedUnrecovered:
                (_port, _portSettled) = (null, true);
                break;
        }

        return outcome;
    }

    /// <summary>Read on first use and after a failed read; otherwise the cache.</summary>
    private async Task<PortState?> CachedPortStateAsync(CancellationToken cancellationToken)
    {
        if (!PortAvailable)
        {
            return null;
        }

        await _portGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _portSettled
                ? _port
                : (await FreshReadLockedAsync("Cooler Boost/şarj limiti", cancellationToken).ConfigureAwait(false)).State;
        }
        finally
        {
            _portGate.Release();
        }
    }

    /// <summary>
    /// Call holding <see cref="_portGate"/>. Reads 0x98 and 0xEF through the port into the cache.
    /// On failure the cache becomes unknown (logged) and the next refresh tries again.
    /// </summary>
    private async Task<(PortState? State, string? Error)> FreshReadLockedAsync(string what, CancellationToken cancellationToken)
    {
        try
        {
            var state = await worker.RunRetryingAsync(ec => new P65Device(ec).ReadPortState(), _retry, log.Warn, cancellationToken)
                .ConfigureAwait(false);
            (_port, _portSettled) = (state, true);
            return (state, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            (_port, _portSettled) = (null, false);
            log.Error($"{what}: port okunamadı", ex);
            return (null, ex.Message);
        }
    }
}
