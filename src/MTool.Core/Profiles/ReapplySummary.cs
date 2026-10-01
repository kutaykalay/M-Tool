using MTool.Core.Ec;

namespace MTool.Core.Profiles;

/// <summary>One outcome that stands for a whole reapply, for the status band and the retry decision.</summary>
public static class ReapplySummary
{
    /// <summary>
    /// The first part that was not written (reapplying stops there), else the last part; null when
    /// nothing was attempted.
    /// </summary>
    public static WriteOutcome? Worst(IReadOnlyList<WriteOutcome> outcomes) =>
        outcomes.FirstOrDefault(o => o.Status is not (WriteStatus.Applied or WriteStatus.DryRun)) ?? outcomes.LastOrDefault();
}
