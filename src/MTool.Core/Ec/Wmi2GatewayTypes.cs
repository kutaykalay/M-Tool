using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Settings;

namespace MTool.Core.Ec;

/// <summary>Which write path a gateway serves; fixed when it is built, and the other path is refused untouched.</summary>
public enum EcGatewayKind
{
    /// <summary>The P65 (WMI1 and the two port registers): <see cref="EcGateway.ApplyAsync"/>.</summary>
    P65Wmi1,

    /// <summary>A WMI2 laptop in experimental mode: <c>Set_Data</c> through <see cref="EcGateway.ApplyWmi2Async"/>.</summary>
    Wmi2Experimental,
}

/// <summary>Why the WMI2 path refused or failed, when neither the rules nor the policy did.</summary>
public enum Wmi2GatewayProblem
{
    /// <summary>A WMI2 write on a P65 gateway.</summary>
    WrongGateway,

    /// <summary>The "before M-Tool" snapshot could not be taken or saved.</summary>
    PreStateNotTaken,

    /// <summary>A snapshot file exists but is unreadable or incomplete; it is never replaced.</summary>
    PreStateUnreadable,

    /// <summary>The snapshot on disk is for another firmware (an update); it is never replaced.</summary>
    PreStateOtherFirmware,

    /// <summary>The EC could not be reached before the write (lock busy, sleep, failed read): nothing changed.</summary>
    Unreachable,

    /// <summary>The written bits did not read back after every attempt (MSI Center may have set it back).</summary>
    ReadBackDiffers,

    /// <summary>The write call or a read after it failed.</summary>
    WriteFailed,

    /// <summary>Between attempts the register came to hold a byte the rules refuse; it was not written again.</summary>
    RegisterMoved,

    /// <summary>"Back to before M-Tool" was cancelled before this register.</summary>
    Cancelled,
}

/// <summary>
/// Everything a WMI2 gateway is built from, fixed for the session (plan asama15 §4.3). The policy is built
/// from these and the gateway's own session state in one place, right before each write.
/// </summary>
/// <param name="Firmware">The running firmware; null when unreadable, which closes every write.</param>
/// <param name="Consented">The user turned on experimental mode for this firmware.</param>
/// <param name="Layout">This session's <see cref="Wmi2LayoutCheck"/> result.</param>
/// <param name="PreState">Where the "before M-Tool" WMI2 snapshot lives (<see cref="PreStateStore.ForWmi2"/>).</param>
/// <param name="IsWmi2Only">
/// Asked right before every write: true only when <c>MSI_ACPI</c> answers and no <c>MSI_Software</c> exists.
/// An exception counts as false. Runs on the EC worker thread, like <paramref name="Now"/>: it must be quick.
/// </param>
/// <param name="DryRun">Validate and log, never write.</param>
public sealed record Wmi2GatewaySetup(
    Wmi2FirmwareList List,
    FirmwareInfo? Firmware,
    bool Consented,
    Wmi2LayoutResult Layout,
    PreStateStore PreState,
    Func<bool> IsWmi2Only,
    bool DryRun,
    Func<DateTimeOffset>? Now = null);

/// <summary>
/// What one WMI2 write did. No user text: the App words <see cref="Rejection"/>, <see cref="Block"/> and
/// <see cref="Problem"/> (15c-5). A refusal names exactly one of the three, except a missing snapshot: then
/// <see cref="Wmi2WriteBlock.NoPreState"/> comes with the <see cref="Problem"/> behind it. <see cref="WriteStatus.Applied"/>
/// without <see cref="Write"/> means the register already held the value. <see cref="WriteStatus.FailedRecovered"/>
/// means the old value was written back and verified; there is no fan table on this path.
/// </summary>
/// <param name="Register">The register the write is for.</param>
/// <param name="Before">What it held right before, when it was read.</param>
/// <param name="Write">The planned byte, when there was one.</param>
public sealed record Wmi2WriteOutcome(
    WriteStatus Status,
    byte Register,
    byte? Before = null,
    RegisterWrite? Write = null,
    Wmi2Rejection? Rejection = null,
    Wmi2WriteBlock? Block = null,
    Wmi2GatewayProblem? Problem = null);
