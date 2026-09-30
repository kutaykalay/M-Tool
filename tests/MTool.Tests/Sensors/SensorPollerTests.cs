using Microsoft.Extensions.Time.Testing;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Sensors;
using MTool.Tests.Fakes;

namespace MTool.Tests.Sensors;

public sealed class SensorPollerTests : IDisposable
{
    private static readonly SensorSnapshot Warm = new(60, 46, 50, 0, 3044, 0);
    private static readonly SensorSnapshot Hot = new(80, 70, 80, 80, 5000, 4800);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero));
    private readonly ListLog _log = new();
    private readonly Queue<Func<Task<SensorSnapshot>>> _answers = new();
    private readonly List<SensorReading> _published = [];
    private int _reads;
    private bool _gateOpen = true;
    private bool _writing;
    private SensorPoller? _poller;

    public void Dispose() => _poller?.Dispose();

    private SensorPoller Poller()
    {
        _poller = new SensorPoller(Read, () => _gateOpen, () => _writing, _time, _log, TimeSpan.FromSeconds(1));
        _poller.ReadingChanged += _published.Add;
        return _poller;
    }

    private Task<SensorSnapshot> Read(CancellationToken cancellationToken)
    {
        _reads++;
        return _answers.Count > 0 ? _answers.Dequeue()() : Task.FromResult(Warm);
    }

    private void Answer(SensorSnapshot snapshot) => _answers.Enqueue(() => Task.FromResult(snapshot));

    private void Fail(Exception? error = null) =>
        _answers.Enqueue(() => Task.FromException<SensorSnapshot>(error ?? new EcAccessException("EC did not answer")));

    // --- one poll ---

    [Fact]
    public void Starts_waiting_with_no_data()
    {
        Poller().Latest.Should().Be(SensorReading.Initial);
    }

    [Fact]
    public async Task A_successful_poll_is_published_as_live()
    {
        var poller = Poller();

        await poller.PollNowAsync();

        poller.Latest.Should().Be(new SensorReading(Warm, SensorStatus.Live, 0, _time.GetUtcNow()));
        _published.Should().Equal(poller.Latest);
    }

    [Fact]
    public async Task A_missed_poll_keeps_the_last_good_values()
    {
        var poller = Poller();
        await poller.PollNowAsync();
        var goodAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromSeconds(1));
        Fail();

        await poller.PollNowAsync();

        poller.Latest.Should().Be(new SensorReading(Warm, SensorStatus.Live, 1, goodAt));
    }

    [Fact]
    public async Task Three_missed_polls_in_a_row_mark_the_data_stale()
    {
        var poller = Poller();
        await poller.PollNowAsync();
        Fail();
        Fail();
        Fail();

        for (var i = 0; i < 3; i++)
        {
            await poller.PollNowAsync();
        }

        poller.Latest.Status.Should().Be(SensorStatus.Stale);
        poller.Latest.Snapshot.Should().Be(Warm);
        poller.Latest.ConsecutiveMisses.Should().Be(3);
    }

    [Fact]
    public async Task A_good_poll_after_stale_data_recovers()
    {
        var poller = Poller();
        Fail();
        Fail();
        Fail();
        Answer(Hot);

        for (var i = 0; i < 4; i++)
        {
            await poller.PollNowAsync();
        }

        poller.Latest.Should().Be(new SensorReading(Hot, SensorStatus.Live, 0, _time.GetUtcNow()));
    }

    [Fact]
    public async Task Missing_temperatures_are_filled_from_the_last_good_poll()
    {
        var poller = Poller();
        Answer(Warm);
        Answer(Hot with { CpuTempC = null, GpuTempC = null });

        await poller.PollNowAsync();
        await poller.PollNowAsync();

        poller.Latest.Snapshot.Should().Be(Hot with { CpuTempC = 60, GpuTempC = 46 });
    }

    [Fact]
    public async Task While_ec_access_is_paused_the_ec_is_not_touched()
    {
        var poller = Poller();
        await poller.PollNowAsync();
        _gateOpen = false;

        await poller.PollNowAsync();

        _reads.Should().Be(1);
        poller.Latest.Status.Should().Be(SensorStatus.Paused);
        poller.Latest.Snapshot.Should().Be(Warm);
    }

    [Fact]
    public async Task A_miss_while_a_write_is_running_does_not_count()
    {
        var poller = Poller();
        await poller.PollNowAsync();
        _writing = true;
        Fail();

        await poller.PollNowAsync();

        poller.Latest.ConsecutiveMisses.Should().Be(0);
        _published.Should().HaveCount(1);
    }

    [Fact]
    public async Task No_exception_escapes_a_poll()
    {
        var poller = Poller();
        Fail(new InvalidOperationException("boom"));

        var act = () => poller.PollNowAsync();

        await act.Should().NotThrowAsync();
        poller.Latest.ConsecutiveMisses.Should().Be(1);
    }

    [Fact]
    public async Task A_read_that_hangs_counts_as_a_miss_after_the_timeout()
    {
        var poller = Poller();
        var hanging = new TaskCompletionSource<SensorSnapshot>();
        _answers.Enqueue(() => hanging.Task);

        var poll = poller.PollNowAsync();
        _time.Advance(SensorPoller.ReadTimeout);
        await poll;

        poller.Latest.ConsecutiveMisses.Should().Be(1);
    }

    [Fact]
    public async Task A_read_that_stays_stuck_is_not_repeated_but_keeps_counting_until_stale()
    {
        var poller = Poller();
        var hanging = new TaskCompletionSource<SensorSnapshot>();
        _answers.Enqueue(() => hanging.Task);
        var first = poller.PollNowAsync();
        _time.Advance(SensorPoller.ReadTimeout);
        await first;

        await poller.PollNowAsync();
        await poller.PollNowAsync();

        _reads.Should().Be(1);
        poller.Latest.Status.Should().Be(SensorStatus.Stale);
        hanging.SetResult(Warm);
    }

    [Fact]
    public async Task A_stuck_read_during_a_write_is_not_counted()
    {
        var poller = Poller();
        var hanging = new TaskCompletionSource<SensorSnapshot>();
        _answers.Enqueue(() => hanging.Task);
        var first = poller.PollNowAsync();
        _time.Advance(SensorPoller.ReadTimeout);
        await first;
        _writing = true;

        await poller.PollNowAsync();

        poller.Latest.ConsecutiveMisses.Should().Be(1);
        hanging.SetResult(Warm);
    }

    [Fact]
    public async Task A_throwing_subscriber_neither_stops_polling_nor_counts_as_a_miss()
    {
        var poller = Poller();
        poller.ReadingChanged += _ => throw new InvalidOperationException("UI gone");
        poller.Start();
        await WaitForReadsAsync(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForReadsAsync(2);

        poller.Latest.ConsecutiveMisses.Should().Be(0);
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("UI gone"));
    }

    [Fact]
    public async Task A_pause_clears_misses_and_old_data_after_it_is_stale_until_a_good_read()
    {
        var poller = Poller();
        await poller.PollNowAsync();
        Fail();
        await poller.PollNowAsync();
        _gateOpen = false;
        await poller.PollNowAsync();
        poller.Latest.ConsecutiveMisses.Should().Be(0);

        _gateOpen = true;
        Fail();
        await poller.PollNowAsync();

        poller.Latest.Status.Should().Be(SensorStatus.Stale);
        await poller.PollNowAsync();
        poller.Latest.Status.Should().Be(SensorStatus.Live);
    }

    [Fact]
    public void Changing_the_interval_after_dispose_is_ignored()
    {
        var poller = Poller();
        poller.Dispose();

        var act = () => poller.SetInterval(TimeSpan.FromSeconds(5));

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Failures_are_logged_only_when_the_state_changes()
    {
        var poller = Poller();
        Fail();
        Fail();
        Fail();
        Fail();

        for (var i = 0; i < 5; i++)
        {
            await poller.PollNowAsync();
        }

        _log.Lines.Where(l => l.StartsWith("WARN")).Should().HaveCount(2); // first miss, then stale
        _log.Lines.Where(l => l.StartsWith("INFO")).Should().ContainSingle(); // recovery
    }

    // --- timer ---

    [Fact]
    public async Task Polls_on_every_tick_of_the_interval()
    {
        var poller = Poller();
        poller.Start();
        await WaitForReadsAsync(1); // immediate first poll

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForReadsAsync(2);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForReadsAsync(3);
    }

    [Fact]
    public async Task The_interval_can_be_changed_while_running()
    {
        var poller = Poller();
        poller.Start();
        await WaitForReadsAsync(1);

        poller.SetInterval(TimeSpan.FromSeconds(5));
        poller.Interval.Should().Be(TimeSpan.FromSeconds(5));
        _time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        _reads.Should().Be(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForReadsAsync(2);
    }

    [Fact]
    public async Task Nothing_is_published_after_dispose()
    {
        var poller = Poller();
        poller.Start();
        await WaitForReadsAsync(1);
        var published = _published.Count;

        poller.Dispose();
        _time.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(50);

        _published.Should().HaveCount(published);
        await poller.PollNowAsync();
        _published.Should().HaveCount(published);
    }

    private async Task WaitForReadsAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref _reads) < count)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, $"{count} reads expected, saw {_reads}");
            await Task.Delay(5);
        }
    }
}
