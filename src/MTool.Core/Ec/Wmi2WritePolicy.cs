namespace MTool.Core.Ec;

/// <summary>What the write is for.</summary>
public enum Wmi2WriteIntent
{
    /// <summary>The write-path test (K5): rewrites the current performance mode and checks it.</summary>
    WritePathTest,

    /// <summary>A real change the user asked for.</summary>
    Change,
}

/// <summary>The condition that closed a WMI2 write; the most basic one when several fail.</summary>
public enum Wmi2WriteBlock
{
    /// <summary>The laptop is not WMI2-only (a WMI1 class exists, or the probe failed).</summary>
    NotWmi2Only,

    /// <summary>The firmware is not an exact entry of the list.</summary>
    NotExactFirmware,

    /// <summary>The user has not turned on experimental mode for this firmware.</summary>
    NoConsent,

    /// <summary>The session's layout check failed or has not run.</summary>
    LayoutNotVerified,

    /// <summary>No "before M-Tool" WMI2 snapshot on disk.</summary>
    NoPreState,

    /// <summary>A write failed earlier in this session.</summary>
    SessionLocked,

    /// <summary>A real change before the write-path test passed (K5).</summary>
    WritePathNotTested,

    /// <summary>The intent is not one M-Tool defines.</summary>
    UnknownIntent,
}

public enum Wmi2WriteVerdictKind
{
    /// <summary>Every condition holds: the write may go to the EC.</summary>
    Write,

    /// <summary>Every condition holds, but nothing is written: the plan is only logged.</summary>
    DryRun,

    /// <summary>A condition is closed; <see cref="Wmi2WriteVerdict.Block"/> names it.</summary>
    Refused,
}

/// <summary>Built only by <see cref="Wmi2WritePolicy.Decide"/>'s three cases: a refusal always names its block.</summary>
public sealed record Wmi2WriteVerdict
{
    private Wmi2WriteVerdict(Wmi2WriteVerdictKind kind, Wmi2WriteBlock? block)
    {
        Kind = kind;
        Block = block;
    }

    public Wmi2WriteVerdictKind Kind { get; }

    public Wmi2WriteBlock? Block { get; }

    public static Wmi2WriteVerdict Write { get; } = new(Wmi2WriteVerdictKind.Write, null);

    public static Wmi2WriteVerdict DryRun { get; } = new(Wmi2WriteVerdictKind.DryRun, null);

    public static Wmi2WriteVerdict Refused(Wmi2WriteBlock block) => new(Wmi2WriteVerdictKind.Refused, block);
}

/// <summary>
/// When a WMI2 experimental write may run at all (plan asama15 §4.4), pure. Every condition alone closes
/// the write; dry-run never opens what a condition closed. The value rules are <see cref="Wmi2WriteRules"/>':
/// a <see cref="Wmi2WriteIntent.WritePathTest"/> verdict counts only for a write that passes
/// <see cref="Wmi2WriteRules.CheckWritePathTest"/>. Six bools sit side by side: build it with named arguments.
/// </summary>
/// <param name="Wmi2Only">The probe found <c>MSI_ACPI</c> and no <c>MSI_Software</c>, checked again right before the write.</param>
/// <param name="FirmwareExact">The running firmware is an exact entry of the list.</param>
/// <param name="Consented">The user turned on experimental mode for this firmware.</param>
/// <param name="LayoutVerified">This session's <see cref="Wmi2LayoutCheck"/> passed.</param>
/// <param name="PreStateSaved">The "before M-Tool" WMI2 snapshot exists on disk.</param>
/// <param name="SessionLockReason">Set when a write failed in this session; any text, even empty, locks.</param>
/// <param name="WritePathTested">The write-path test passed in this session (K5).</param>
/// <param name="DryRun">Validate and log, never write.</param>
public sealed record Wmi2WritePolicy(
    bool Wmi2Only,
    bool FirmwareExact,
    bool Consented,
    bool LayoutVerified,
    bool PreStateSaved,
    string? SessionLockReason,
    bool WritePathTested,
    bool DryRun)
{
    public Wmi2WriteVerdict Decide(Wmi2WriteIntent intent)
    {
        if (FirstBlock() is { } block)
        {
            return Wmi2WriteVerdict.Refused(block);
        }

        if (!Enum.IsDefined(intent))
        {
            return Wmi2WriteVerdict.Refused(Wmi2WriteBlock.UnknownIntent);
        }

        // Nothing is written in dry-run, so a planned change may be shown before the write-path test.
        if (DryRun)
        {
            return Wmi2WriteVerdict.DryRun;
        }

        return intent == Wmi2WriteIntent.Change && !WritePathTested
            ? Wmi2WriteVerdict.Refused(Wmi2WriteBlock.WritePathNotTested)
            : Wmi2WriteVerdict.Write;
    }

    private Wmi2WriteBlock? FirstBlock() =>
        !Wmi2Only ? Wmi2WriteBlock.NotWmi2Only
        : !FirmwareExact ? Wmi2WriteBlock.NotExactFirmware
        : !Consented ? Wmi2WriteBlock.NoConsent
        : !LayoutVerified ? Wmi2WriteBlock.LayoutNotVerified
        : !PreStateSaved ? Wmi2WriteBlock.NoPreState
        : SessionLockReason is not null ? Wmi2WriteBlock.SessionLocked
        : null;
}
