using Microsoft.Extensions.Time.Testing;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Sensors;
using MTool.Tests.Fakes;

namespace MTool.Tests.Sensors;

/// <summary>
/// The sensor poller and the WMI2 breaker together (docs/plans/asama10.md, Adım 5): a hung round is
/// never repeated by the poller, so it reaches the breaker once, when it finally fails.
/// </summary>
public sealed class Wmi2PollerBreakerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly Wmi2CircuitBreaker _breaker = new();
    private readonly TaskCompletionSource<SensorSnapshot> _hung = new();
    private int _reads;
    private SensorPoller? _poller;

    public void Dispose() => _poller?.Dispose();

    [Fact]
    public async Task A_hung_round_reaches_the_breaker_once()
    {
        _poller = new SensorPoller(
            ct => _breaker.RunAsync(_ => { _reads++; return _hung.Task; }, ct),
            () => true, () => false, _time, new ListLog(), Wmi2ReadPolicy.MinInterval);

        var first = _poller.PollNowAsync(); // starts the round, which times out on the poller's side
        _time.Advance(SensorPoller.ReadTimeout);
        await first;
        for (var i = 0; i < 3; i++)
        {
            await _poller.PollNowAsync();
        }

        _reads.Should().Be(1, "the poller does not repeat a round still running");
        _breaker.IsOpen.Should().BeFalse("the poller's own timeout is not a finished round");

        _hung.SetException(new EcAccessException("MSI_ACPI Get_Data(0x68): no answer within 2 s (timeout)."));
        _breaker.LastError.Should().Contain("timeout", "the hung round counted when it ended");
        _breaker.IsOpen.Should().BeFalse("one finished round, one failure");

        await _poller.PollNowAsync();

        _reads.Should().Be(2, "the next poll starts a new round once the old one has ended");
    }
}
