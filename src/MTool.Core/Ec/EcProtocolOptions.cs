namespace MTool.Core.Ec;

/// <param name="MaxAttempts">Whole-transaction retries before a read is reported as failed.</param>
/// <param name="MaxStatusPolls">
/// Optional cap on status-port polls per step. Null in production, where only
/// <see cref="StepTimeout"/> limits a step; tests set it so a simulated EC fails fast.
/// </param>
/// <param name="StepTimeout">
/// Wall-clock limit for one step. On the P65 a missing answer does not arrive late, so waiting
/// longer does not help: a 2/5/10/20 ms sweep gave the same failure rate (~2.5% of dump-sized
/// read cycles), 200 ms was worse. The EC has episodes in which it takes addresses but never
/// answers; they outlast all retries (stress runs, 2026-09-30).
/// </param>
/// <param name="SettleClearPolls">
/// After an unanswered read, consecutive polls with an empty output buffer required before retrying,
/// so a late answer to the abandoned read is drained instead of being taken as the next answer.
/// </param>
public sealed record EcProtocolOptions(
    int MaxAttempts = 5,
    int? MaxStatusPolls = null,
    TimeSpan? StepTimeout = null,
    int SettleClearPolls = 20)
{
    public static EcProtocolOptions Default { get; } = new();

    public TimeSpan EffectiveStepTimeout => StepTimeout ?? TimeSpan.FromMilliseconds(20);

    public int EffectiveMaxStatusPolls => MaxStatusPolls ?? int.MaxValue;
}
