namespace MTool.Core.Ec;

public sealed record RegisterWrite(byte Register, byte Value)
{
    public override string ToString() => $"0x{Register:X2}=0x{Value:X2}";
}

/// <summary>A named set of register writes applied together by <see cref="EcGateway"/>.</summary>
public sealed record WritePlan(string Description, IReadOnlyList<RegisterWrite> Writes);

/// <param name="FirmwareSupported">EC firmware is exactly the verified one.</param>
/// <param name="PreStateSaved">The "before M-Tool" snapshot exists on disk.</param>
/// <param name="DryRun">Validate and log, never write.</param>
/// <param name="PortAvailable">
/// Cooler Boost and the charge limit can be reached (the raw port is open). When false, plans for
/// them are refused before any EC access and recovery never reaches for Cooler Boost.
/// </param>
/// <param name="PersistedLockReason">Set when an earlier session's write failed; writes stay locked until cleared by hand.</param>
public sealed record WritePolicy(
    bool FirmwareSupported, bool PreStateSaved, bool DryRun, bool PortAvailable, string? PersistedLockReason = null);

public enum WriteStatus
{
    /// <summary>Validation failed or writes are locked; nothing was written.</summary>
    Rejected,

    /// <summary>Validated and logged; nothing was written.</summary>
    DryRun,

    /// <summary>Written and verified.</summary>
    Applied,

    /// <summary>A write failed; the fan table was confirmed safe afterwards. Writes are now locked.</summary>
    FailedRecovered,

    /// <summary>A write failed and a safe fan table could not be confirmed. Writes are now locked.</summary>
    FailedUnrecovered,
}

public sealed record WriteOutcome(WriteStatus Status, IReadOnlyList<RegisterWrite> Planned, string Message);
