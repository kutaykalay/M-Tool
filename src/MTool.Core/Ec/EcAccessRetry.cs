namespace MTool.Core.Ec;

/// <summary>
/// How the gateway rides out a silent EC period: after an <see cref="EcAccessException"/> the same
/// access is repeated after each delay in turn. The stress test (2026-09-30) measured silent
/// periods of 117-252 ms, mostly on the first access after the EC was idle; the default window
/// (750 ms plus the protocol's own attempts) covers that about three times over.
/// </summary>
/// <param name="Sleep">Waits on the EC worker thread; replaceable in tests.</param>
/// <param name="SleepBudget">
/// Total waiting allowed for one plan, recovery included. The Access_EC lock is held meanwhile, so
/// a dead EC must not keep Windows' own EC users (battery, thermal, hotkeys) waiting for long.
/// </param>
public sealed record EcAccessRetry(IReadOnlyList<TimeSpan> Delays, Action<TimeSpan> Sleep, TimeSpan SleepBudget)
{
    public static EcAccessRetry Default { get; } = new(
        Array.AsReadOnly(new[] { 50, 100, 200, 400 }.Select(ms => TimeSpan.FromMilliseconds(ms)).ToArray()),
        Thread.Sleep,
        TimeSpan.FromMilliseconds(1500));
}
