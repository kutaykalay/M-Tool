using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Core.Device;

public enum WriteMode
{
    Enabled,
    DryRun,
    Locked,
}

/// <param name="Firmware">Null when it could not be read.</param>
/// <param name="LockReason">Why writes are refused; null unless <see cref="WriteMode.Locked"/>.</param>
public sealed record DeviceAccess(FirmwareInfo? Firmware, WriteMode WriteMode, string? LockReason);

/// <summary>
/// Everything the UI may do with the laptop. No register addresses, no write plans: every write
/// still goes through <see cref="EcGateway"/>. Writes never throw for EC trouble; they report it
/// in the <see cref="WriteOutcome"/>.
/// </summary>
public interface IP65Control
{
    /// <summary>Current state; a failed write turns it to <see cref="WriteMode.Locked"/>.</summary>
    DeviceAccess Access { get; }

    /// <summary>One attempt, no retry: a poll that hits a silent EC period throws and is skipped.</summary>
    Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken = default);

    /// <summary>Rides out silent EC periods like the gateway does.</summary>
    Task<ControlState> ReadControlStateAsync(CancellationToken cancellationToken = default);

    Task<WriteOutcome> ApplyFanProfileAsync(FanProfile profile, CancellationToken cancellationToken = default);

    Task<WriteOutcome> SetCoolerBoostAsync(bool on, CancellationToken cancellationToken = default);

    Task<WriteOutcome> SetPerformanceAsync(PerformanceMode mode, CancellationToken cancellationToken = default);

    Task<WriteOutcome> SetChargeLimitAsync(int percent, CancellationToken cancellationToken = default);

    Task<WriteOutcome> SetFanModeAsync(FanMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes every set part of <paramref name="desired"/> (fan table first) and stops at the first
    /// part that was not written (rejected or failed). One outcome per attempted part.
    /// </summary>
    Task<IReadOnlyList<WriteOutcome>> ApplyDesiredAsync(
        DesiredState desired, ProfileCatalog catalog, CancellationToken cancellationToken = default);
}
