using MTool.Core.Device.Config;

namespace MTool.Core.Diagnostics;

/// <summary>Where the interface probe stopped.</summary>
public enum WmiProbeStage
{
    /// <summary>Connecting to <c>root\WMI</c>.</summary>
    Connect,

    /// <summary>Looking for the WMI1 class <c>MSI_Software</c>.</summary>
    QuerySoftware,

    /// <summary>Looking for the WMI2 class <c>MSI_ACPI</c>.</summary>
    QueryAcpi,
}

/// <summary>How the probe ended: <see cref="Ok"/> is an answer, even "no MSI class".</summary>
public enum WmiProbeStatus
{
    /// <summary>WMI answered: an interface, or no MSI class.</summary>
    Ok,

    /// <summary>A step failed with a code.</summary>
    Failed,

    /// <summary>A step did not answer in time.</summary>
    Timeout,
}

/// <summary>A probe step that gave no answer. Only the stage and the number are kept, never Windows' message.</summary>
/// <param name="Code">The WMI status or HRESULT (<c>0x8004100C</c> is "not supported"); null when the step did not answer in time.</param>
public sealed record WmiFailure(WmiProbeStage Stage, uint? Code)
{
    public bool TimedOut => Code is null;

    public string CodeText => Code is { } code ? $"0x{code:X8}" : "timeout";

    public override string ToString() => $"{Stage} {CodeText}";
}

/// <summary>One class query: found or not, or no answer at all.</summary>
public sealed record WmiClassAnswer(bool Found, WmiFailure? Failure)
{
    public static WmiClassAnswer Of(bool found) => new(found, null);

    public static WmiClassAnswer Failed(WmiFailure failure) => new(false, failure);
}

/// <summary>
/// What the interface probe found, keeping "no MSI class" (<see cref="Interface"/> null, no
/// <see cref="Failure"/>) apart from "WMI did not answer" (a <see cref="Failure"/>). Never both an
/// interface and a failure: a failure means the interface is unknown.
/// </summary>
public sealed record WmiProbeOutcome
{
    private WmiProbeOutcome(WmiInterface? @interface, WmiFailure? failure) => (Interface, Failure) = (@interface, failure);

    public WmiInterface? Interface { get; }

    public WmiFailure? Failure { get; }

    public WmiProbeStatus Status => Failure switch
    {
        null => WmiProbeStatus.Ok,
        { TimedOut: true } => WmiProbeStatus.Timeout,
        _ => WmiProbeStatus.Failed,
    };

    /// <param name="interface">The interface found, or null for no MSI class.</param>
    public static WmiProbeOutcome Answered(WmiInterface? @interface) => new(@interface, null);

    public static WmiProbeOutcome Failed(WmiFailure failure) => new(null, failure);

    /// <summary>WMI1 wins when both exist: it is the interface M-Tool was verified on.</summary>
    public static WmiInterface? Classify(bool hasSoftware, bool hasAcpi) =>
        hasSoftware ? WmiInterface.Wmi1 : hasAcpi ? WmiInterface.Wmi2 : null;

    /// <summary>
    /// The two class queries, each on its own: a failed <c>MSI_ACPI</c> query does not lose a found
    /// WMI1, but a failed <c>MSI_Software</c> query is never read as WMI2, since WMI1 would have won.
    /// </summary>
    public static WmiProbeOutcome Decide(WmiClassAnswer software, WmiClassAnswer acpi) => (software, acpi) switch
    {
        ({ Failure: { } failure }, _) => Failed(failure),
        ({ Found: true }, _) => Answered(WmiInterface.Wmi1),
        (_, { Failure: { } failure }) => Failed(failure),
        _ => Answered(Classify(hasSoftware: false, acpi.Found)),
    };
}
