using Microsoft.Extensions.Time.Testing;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Power;

public sealed class PowerSourceSwitcherTests : IDisposable
{
    private static readonly PowerSwitchOptions Options = PowerSwitchOptions.Default;
    private static readonly TimeSpan Debounce = Options.Debounce;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 15, 0, 0, TimeSpan.Zero));
    private readonly FakePowerEvents _events = new();
    private readonly FakePowerSource _source = new();
    private readonly ListLog _log = new();
    private readonly PowerStateCoordinator _coordinator;
    private readonly List<(PowerSource Source, bool Write)> _calls = [];
    private readonly List<AutoReapplyResult> _results = [];
    private Func<PowerSource, bool, Task<IReadOnlyList<WriteOutcome>?>>? _answer;
    private PowerSourceSwitcher? _switcher;

    public PowerSourceSwitcherTests() => _coordinator = new PowerStateCoordinator(_time, AutoReapplyOptions.Default.GateDelay);

    public void Dispose() => _switcher?.Dispose();

    private Task<IReadOnlyList<WriteOutcome>?> Switch(PowerSource source, bool write)
    {
        _calls.Add((source, write));
        return _answer?.Invoke(source, write) ?? Task.FromResult<IReadOnlyList<WriteOutcome>?>(Outcomes(WriteStatus.Applied));
    }

    private static IReadOnlyList<WriteOutcome> Outcomes(WriteStatus status) => [new WriteOutcome(status, [], status.ToString())];

    private PowerSourceSwitcher Started(Func<PowerSource, bool, Task<IReadOnlyList<WriteOutcome>?>>? @switch = null)
    {
        _switcher = new PowerSourceSwitcher(@switch ?? Switch, _source, _events, _coordinator, _time, _log, Options);
        _switcher.Switched += _results.Add;
        _switcher.Start();
        _calls.Clear();
        _results.Clear();
        return _switcher;
    }

    [Fact]
    public void Start_reads_the_source_once_and_subscribes()
    {
        _switcher = new PowerSourceSwitcher(Switch, _source, _events, _coordinator, _time, _log, Options);

        _switcher.Start();

        _calls.Should().Equal((PowerSource.Ac, true));
        _source.HasSubscribers.Should().BeTrue();
        _events.HasSubscribers.Should().BeTrue();
    }

    [Fact]
    public void A_change_is_acted_on_only_after_the_debounce()
    {
        Started();

        _source.Set(PowerSource.Battery);
        _time.Advance(Debounce - TimeSpan.FromMilliseconds(1));
        _calls.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMilliseconds(1));
        _calls.Should().Equal((PowerSource.Battery, true));
    }

    [Fact]
    public void Several_changes_within_the_debounce_give_one_call_with_the_last_source()
    {
        Started();

        for (var i = 0; i < 5; i++)
        {
            _source.Set(i % 2 == 0 ? PowerSource.Battery : PowerSource.Ac);
            _time.Advance(TimeSpan.FromMilliseconds(500));
        }

        _time.Advance(Debounce);
        _calls.Should().Equal((PowerSource.Battery, true));
    }

    [Fact]
    public void An_unknown_source_is_not_acted_on()
    {
        Started();

        _source.Set(null);
        _time.Advance(Debounce);

        _calls.Should().BeEmpty();
    }

    [Fact]
    public void A_change_while_the_gate_is_shut_updates_without_writing()
    {
        Started();
        // The gate is shut by AutoReapplier in the app; here by hand, as on a wake.
        _coordinator.OnSuspend();
        _coordinator.OnResume();

        _source.Set(PowerSource.Battery);
        _time.Advance(Debounce);

        _coordinator.IsEcAccessAllowed.Should().BeFalse("the gate stays shut 5 s after wake");
        _calls.Should().Equal((PowerSource.Battery, false));
    }

    [Fact]
    public void Sleep_cancels_a_waiting_change()
    {
        Started();

        _source.Set(PowerSource.Battery);
        _events.Suspend();
        _time.Advance(Debounce);

        _calls.Should().BeEmpty();
    }

    [Fact]
    public void Wake_reads_the_source_at_once_without_writing()
    {
        Started();
        _events.Suspend();
        _source.Current = PowerSource.Battery;

        _events.Resume();

        _calls.Should().Equal((PowerSource.Battery, false));
    }

    [Fact]
    public void A_result_is_reported_only_when_something_was_written()
    {
        _answer = (_, write) => Task.FromResult<IReadOnlyList<WriteOutcome>?>(write ? Outcomes(WriteStatus.Rejected) : null);
        Started();

        _events.Suspend();
        _events.Resume();
        _results.Should().BeEmpty();

        _time.Advance(TimeSpan.FromSeconds(10));
        _source.Set(PowerSource.Battery);
        _time.Advance(Debounce);

        _results.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new AutoReapplyResult(ReapplyTrigger.PowerSource, Outcomes(WriteStatus.Rejected)));
    }

    [Fact]
    public void A_failing_switch_is_logged_and_the_next_change_still_runs()
    {
        _answer = (source, _) => source == PowerSource.Battery
            ? Task.FromException<IReadOnlyList<WriteOutcome>?>(new InvalidOperationException("boom"))
            : Task.FromResult<IReadOnlyList<WriteOutcome>?>(null);
        Started();

        _source.Set(PowerSource.Battery);
        _time.Advance(Debounce);
        _source.Set(PowerSource.Ac);
        _time.Advance(Debounce);

        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("boom"));
        _calls.Should().Equal((PowerSource.Battery, true), (PowerSource.Ac, true));
    }

    [Fact]
    public void A_subscriber_that_throws_is_logged()
    {
        Started();
        _switcher!.Switched += _ => throw new InvalidOperationException("abone");

        _source.Set(PowerSource.Battery);
        _time.Advance(Debounce);

        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("abone"));
    }

    [Fact]
    public void A_source_that_throws_on_read_is_logged_not_thrown()
    {
        Started();
        var throwing = new ThrowingSource();
        _switcher!.Dispose();
        _switcher = new PowerSourceSwitcher(Switch, throwing, _events, _coordinator, _time, _log, Options);

        var start = () => _switcher.Start();

        start.Should().NotThrow();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR"));
    }

    [Fact]
    public void After_dispose_nothing_runs_and_the_events_are_left()
    {
        var switcher = Started();
        _source.Set(PowerSource.Battery);

        switcher.Dispose();
        _time.Advance(Debounce);
        _events.Resume();

        _calls.Should().BeEmpty();
        _source.HasSubscribers.Should().BeFalse();
        _events.HasSubscribers.Should().BeFalse();
    }

    [Fact]
    public void Is_running_while_a_switch_has_not_returned()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<WriteOutcome>?>();
        var switcher = Started((_, _) => pending.Task);

        _source.Set(PowerSource.Battery);
        _time.Advance(Debounce);
        switcher.IsRunning.Should().BeTrue();

        pending.SetResult(null);
        SpinWait.SpinUntil(() => !switcher.IsRunning, TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void A_second_start_does_nothing()
    {
        var switcher = Started();

        switcher.Start();

        _calls.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_escapes_even_when_the_log_fails()
    {
        _switcher = new PowerSourceSwitcher(
            (_, _) => Task.FromException<IReadOnlyList<WriteOutcome>?>(new InvalidOperationException("boom")),
            _source, _events, _coordinator, _time, new ThrowingLog(), Options);

        var start = () => _switcher.Start();

        start.Should().NotThrow();
    }

    [Fact]
    public void A_debounce_of_zero_or_less_is_refused()
    {
        var create = () => new PowerSourceSwitcher(Switch, _source, _events, _coordinator, _time, _log, new PowerSwitchOptions(TimeSpan.Zero));

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task A_cable_pulled_during_sleep_gives_one_write_on_wake_with_the_battery_pair()
    {
        // The real service and the real reapplier, as wired in the app, on the same fake clock.
        var folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
        try
        {
            var control = new FakeP65Control();
            var settings = AppSettings.Default with
            {
                Desired = new DesiredState("Cool", PerformanceMode.High),
                PowerSwitch = new PowerSwitchSettings(true, new PowerProfilePair("Cool", PerformanceMode.High), new PowerProfilePair("Silent", PerformanceMode.Eco)),
            };
            var service = new ProfileService(control, ProfileCatalog.BuiltIn, new SettingsStore(folder), settings, _log);
            await service.SwitchPowerSourceAsync(PowerSource.Ac, write: false);
            using var reapplier = new AutoReapplier(service.ReapplyAsync, _events, _coordinator, _time, _log, AutoReapplyOptions.Default);
            reapplier.Start();
            Started(service.SwitchPowerSourceAsync);
            control.Calls.Clear();

            _events.Suspend();
            _source.Current = PowerSource.Battery;
            _events.Resume();
            _time.Advance(AutoReapplyOptions.Default.ResumeDelay);
            SpinWait.SpinUntil(() => !control.Calls.IsEmpty, TimeSpan.FromSeconds(5));

            control.Calls.Should().Equal($"desired {new DesiredState("Silent", PerformanceMode.Eco)}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class ThrowingLog : MTool.Core.IAppLog
    {
        public void Info(string message) => throw new IOException("log dolu");

        public void Warn(string message) => throw new IOException("log dolu");

        public void Error(string message, Exception? exception = null) => throw new IOException("log dolu");
    }

    private sealed class ThrowingSource : IPowerSource
    {
        public PowerSource? Current => throw new InvalidOperationException("okunamadı");

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
