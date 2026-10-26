using MTool.App.Hardware;
using MTool.App.Resources;
using MTool.Core.Ec;

namespace MTool.App.ViewModels;

/// <summary>What "Back to before M-Tool" did: one band for the whole, and a short line per register (none when all share one refusal).</summary>
internal sealed record Wmi2RevertText(string Summary, MessageKind Kind, IReadOnlyList<string> Lines);

/// <summary>
/// The words for what an experimental WMI2 write did (plan asama15 15c-5). Core hands over enums only. A
/// reason the user cannot act on (a programming error the gateway still guards against) shares one sentence
/// with the enum's name, so a report can say which it was. Never error text from an exception.
/// </summary>
internal static class Wmi2OutcomeText
{
    /// <param name="preStatePath">The "before M-Tool" snapshot file, named when it is the problem.</param>
    public static string Of(Wmi2WriteOutcome outcome, string preStatePath) => outcome.Status switch
    {
        WriteStatus.Applied => outcome.Write is null ? Strings.Wmi2_OutcomeAlreadySet : Strings.Wmi2_OutcomeApplied,
        WriteStatus.DryRun => Strings.Wmi2_OutcomeDryRun,
        WriteStatus.FailedRecovered => string.Format(Strings.Wmi2_OutcomeRecovered, WhyItFailed(outcome.Problem)),
        WriteStatus.FailedUnrecovered => string.Format(Strings.Wmi2_OutcomeUnrecovered, WhyItFailed(outcome.Problem)),
        _ => Refusal(outcome, preStatePath),
    };

    public static string Of(Wmi2ControlUnavailable reason) => reason switch
    {
        Wmi2ControlUnavailable.NotWmi2 => Strings.Wmi2_ControlNotWmi2,
        Wmi2ControlUnavailable.FirmwareUnreadable => Strings.Wmi2_ControlFirmwareUnreadable,
        Wmi2ControlUnavailable.OtherFirmware => Strings.Wmi2_ControlFirmwareChanged,
        _ => Strings.Wmi2_ControlNotExact,
    };

    /// <summary>
    /// "Back to before M-Tool": what to do and that writes are off is said once, in the band, by the worst outcome;
    /// each line only says how its register went. The same refusal for every register is the band alone.
    /// </summary>
    public static Wmi2RevertText Revert(IReadOnlyList<Wmi2WriteOutcome> outcomes, string preStatePath)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var lines = outcomes.Select(o => RevertLine(o, preStatePath)).ToArray();
        if (outcomes.Any(o => o.Status == WriteStatus.FailedUnrecovered))
        {
            return new(Strings.Wmi2_RevertUnrecovered, MessageKind.Warning, lines);
        }

        if (outcomes.Any(o => o.Status == WriteStatus.FailedRecovered))
        {
            return new(Strings.Wmi2_RevertRecovered, MessageKind.Warning, lines);
        }

        if (outcomes.Any(o => o.Status == WriteStatus.Rejected))
        {
            var refusals = outcomes.Select(o => o.Status == WriteStatus.Rejected ? Refusal(o, preStatePath) : null).Distinct().ToArray();
            return refusals is [{ } only]
                ? new(only, MessageKind.Warning, [])
                : new(Strings.Wmi2_RevertRefused, MessageKind.Warning, lines);
        }

        return outcomes.Any(o => o.Status == WriteStatus.DryRun)
            ? new(Strings.Wmi2_OutcomeDryRun, MessageKind.Info, lines)
            : new(Strings.Wmi2_RevertDone, MessageKind.Info, lines);
    }

    /// <summary>One line of "Back to before M-Tool": what was written back, and how that register went.</summary>
    public static string RevertLine(Wmi2WriteOutcome outcome, string preStatePath) =>
        string.Format(Strings.Wmi2_RevertLine, RegisterName(outcome.Register), outcome.Status switch
        {
            WriteStatus.Applied => outcome.Write is null ? Strings.Wmi2_RevertLineAlreadySet : Strings.Wmi2_RevertLineRestored,
            WriteStatus.DryRun => Strings.Wmi2_RevertLineDryRun,
            WriteStatus.FailedRecovered or WriteStatus.FailedUnrecovered => string.Format(Strings.Wmi2_RevertLineFailed, WhyItFailed(outcome.Problem)),
            _ => Refusal(outcome, preStatePath),
        });

    /// <summary>A write that held, or a dry run, is information; anything else is a warning.</summary>
    public static MessageKind KindOf(Wmi2WriteOutcome outcome) =>
        outcome.Status is WriteStatus.Applied or WriteStatus.DryRun ? MessageKind.Info : MessageKind.Warning;

    /// <summary>The problem comes first: a missing snapshot is reported with the file problem behind it.</summary>
    private static string Refusal(Wmi2WriteOutcome outcome, string preStatePath) =>
        outcome.Problem is { } problem ? Problem(problem, preStatePath)
        : outcome.Block is { } block ? Block(block)
        : outcome.Rejection is { } rejection ? Rejection(rejection)
        : Internal(outcome.Status);

    private static string Problem(Wmi2GatewayProblem problem, string preStatePath) => problem switch
    {
        Wmi2GatewayProblem.PreStateNotTaken => Strings.Wmi2_ProblemPreStateNotTaken,
        Wmi2GatewayProblem.PreStateUnreadable => string.Format(Strings.Wmi2_ProblemPreStateUnreadable, preStatePath),
        Wmi2GatewayProblem.PreStateOtherFirmware => string.Format(Strings.Wmi2_ProblemPreStateOtherFirmware, preStatePath),
        Wmi2GatewayProblem.Unreachable => Strings.Wmi2_ProblemUnreachable,
        Wmi2GatewayProblem.ReadBackDiffers => Strings.Wmi2_ProblemReadBack,
        Wmi2GatewayProblem.WriteFailed => Strings.Wmi2_ProblemWriteFailed,
        Wmi2GatewayProblem.RegisterMoved => Strings.Wmi2_ProblemRegisterMoved,
        Wmi2GatewayProblem.Cancelled => Strings.Wmi2_ProblemCancelled,
        _ => Internal(problem),
    };

    private static string Block(Wmi2WriteBlock block) => block switch
    {
        Wmi2WriteBlock.NotWmi2Only => Strings.Wmi2_RefusedNotWmi2Only,
        Wmi2WriteBlock.NotExactFirmware => Strings.Wmi2_ControlNotExact,
        Wmi2WriteBlock.LayoutNotVerified => Strings.Wmi2_RefusedOutsideLayout,
        Wmi2WriteBlock.NoPreState => Strings.Wmi2_RefusedNoPreState,
        Wmi2WriteBlock.SessionLocked => Strings.Wmi2_RefusedLocked,
        Wmi2WriteBlock.WritePathNotTested => Strings.Wmi2_RefusedWritePathTest,
        _ => Internal(block),
    };

    private static string Rejection(Wmi2Rejection rejection) => rejection switch
    {
        Wmi2Rejection.NotExactFirmware => Strings.Wmi2_ControlNotExact,
        Wmi2Rejection.ValueNotAllowed => Strings.Wmi2_RefusedValue,
        Wmi2Rejection.CurrentOutsideLayout => Strings.Wmi2_RefusedOutsideLayout,
        Wmi2Rejection.ListInvalid => Strings.Wmi2_RefusedListInvalid,
        _ => Internal(rejection),
    };

    /// <summary>A failed write always has a reason to give; one the gateway left out reads as a failed write.</summary>
    private static string WhyItFailed(Wmi2GatewayProblem? problem) => problem switch
    {
        Wmi2GatewayProblem.ReadBackDiffers => Strings.Wmi2_ProblemReadBack,
        Wmi2GatewayProblem.RegisterMoved => Strings.Wmi2_ProblemRegisterMoved,
        _ => Strings.Wmi2_ProblemWriteFailed,
    };

    private static string Internal(Enum reason) => string.Format(Strings.Wmi2_OutcomeInternal, reason);

    private static string RegisterName(byte register) => register switch
    {
        Wmi2WriteCeiling.CoolerBoostRegister => Strings.Wmi2_CoolerBoost,
        Wmi2WriteCeiling.PerformanceRegister => Strings.Wmi2_PerformanceMode,
        Wmi2WriteCeiling.FanModeRegister => Strings.Wmi2_FanMode,
        Wmi2WriteCeiling.ChargeLimitRegister => Strings.Wmi2_ChargeLimit,
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"0x{register:X2}"),
    };
}
