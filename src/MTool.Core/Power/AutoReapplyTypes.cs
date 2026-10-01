using MTool.Core.Ec;

namespace MTool.Core.Power;

public enum ReapplyTrigger
{
    Startup,
    Resume,
    Retry,
}

/// <param name="ResumeDelay">Wait after wake before reapplying; the same delay the EC access gate uses.</param>
/// <param name="RetryDelay">Wait before the single retry after a rejected reapply.</param>
public sealed record AutoReapplyOptions(TimeSpan ResumeDelay, TimeSpan RetryDelay)
{
    public static AutoReapplyOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
}

public sealed record AutoReapplyResult(ReapplyTrigger Trigger, IReadOnlyList<WriteOutcome> Outcomes);
