namespace MTool.Core.Ec;

/// <param name="MaxAttempts">Whole-transaction retries before a read is reported as failed.</param>
/// <param name="MaxStatusPolls">Status-port polls allowed while waiting for IBF/OBF in one step.</param>
/// <param name="StepTimeout">Wall-clock limit for one step; whichever of the two limits hits first wins.</param>
/// <param name="SettleClearPolls">
/// After a failed attempt, consecutive polls with an empty output buffer required before retrying,
/// so a late answer to the abandoned read is drained instead of being taken as the next answer.
/// </param>
public sealed record EcProtocolOptions(
    int MaxAttempts = 5,
    int MaxStatusPolls = 1000,
    TimeSpan? StepTimeout = null,
    int SettleClearPolls = 20)
{
    public static EcProtocolOptions Default { get; } = new();

    public TimeSpan EffectiveStepTimeout => StepTimeout ?? TimeSpan.FromMilliseconds(20);
}
