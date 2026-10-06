using MTool.Core.Ec;

namespace MTool.Core.Power;

public enum ReapplyTrigger
{
    Startup,
    Resume,
    Retry,

    /// <summary>The laptop moved between AC and battery; see <see cref="PowerSourceSwitcher"/>.</summary>
    PowerSource,
}

/// <param name="GateDelay">How long the EC access gate stays shut after wake; see <see cref="PowerStateCoordinator"/>.</param>
/// <param name="ResumeDelay">
/// Wait after wake before reapplying; must be longer than <paramref name="GateDelay"/>. A Windows timer runs on
/// the coarse tick count (~15.6 ms steps), so it can fire that much early against the gate's high-resolution clock.
/// </param>
/// <param name="RetryDelay">Wait before the single retry after a rejected reapply.</param>
public sealed record AutoReapplyOptions(TimeSpan GateDelay, TimeSpan ResumeDelay, TimeSpan RetryDelay)
{
    public static AutoReapplyOptions Default { get; } =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(30));
}

public sealed record AutoReapplyResult(ReapplyTrigger Trigger, IReadOnlyList<WriteOutcome> Outcomes);
