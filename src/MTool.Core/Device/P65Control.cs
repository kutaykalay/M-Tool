using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

/// <summary><see cref="IP65Control"/> over the EC worker (reads) and the gateway (writes).</summary>
public sealed class P65Control(EcWorker worker, WriteAccessSetup setup, IAppLog log, EcAccessRetry? retry = null) : IP65Control
{
    private readonly EcAccessRetry _retry = retry ?? EcAccessRetry.Default;

    public DeviceAccess Access => setup.Gateway.LockReason is { } reason
        ? new DeviceAccess(setup.Firmware, WriteMode.Locked, reason)
        : new DeviceAccess(setup.Firmware, setup.Gateway.IsDryRun ? WriteMode.DryRun : WriteMode.Enabled, null);

    public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken = default) =>
        worker.RunAsync(ec => new P65Device(ec).ReadSensors(), cancellationToken);

    public Task<ControlState> ReadControlStateAsync(CancellationToken cancellationToken = default) =>
        worker.RunRetryingAsync(ec => new P65Device(ec).ReadControlState(), _retry, log.Warn, cancellationToken);

    public Task<WriteOutcome> ApplyFanProfileAsync(FanProfile profile, CancellationToken cancellationToken = default) =>
        setup.Gateway.ApplyAsync(WritePlans.FanCurves(profile.Curves, profile.Name), cancellationToken);

    /// <summary>The gateway checks again under the EC lock that only bit 7 changes.</summary>
    public async Task<WriteOutcome> SetCoolerBoostAsync(bool on, CancellationToken cancellationToken = default)
    {
        byte current;
        try
        {
            current = await worker.RunRetryingAsync(ec => ec.Read(EcMap.CoolerBoost), _retry, log.Warn, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error("Cooler Boost: register okunamadı, yazılmadı", ex);
            return new WriteOutcome(WriteStatus.Rejected, [], $"EC erişilemedi: {ex.Message}");
        }

        return await setup.Gateway.ApplyAsync(WritePlans.CoolerBoost(on, current), cancellationToken).ConfigureAwait(false);
    }

    public Task<WriteOutcome> SetPerformanceAsync(PerformanceMode mode, CancellationToken cancellationToken = default) =>
        setup.Gateway.ApplyAsync(WritePlans.Performance(mode), cancellationToken);

    public Task<WriteOutcome> SetChargeLimitAsync(int percent, CancellationToken cancellationToken = default) =>
        setup.Gateway.ApplyAsync(WritePlans.ChargeLimit(percent), cancellationToken);

    public Task<WriteOutcome> SetFanModeAsync(FanMode mode, CancellationToken cancellationToken = default) =>
        setup.Gateway.ApplyAsync(WritePlans.Fan(mode), cancellationToken);

    public async Task<IReadOnlyList<WriteOutcome>> ApplyDesiredAsync(
        DesiredState desired, ProfileCatalog catalog, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<WriteOutcome>();
        foreach (var plan in desired.ToPlans(catalog))
        {
            var outcome = await setup.Gateway.ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
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
}
