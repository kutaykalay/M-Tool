using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public sealed class P65ControlTests : IDisposable
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false);
    private static readonly FirmwareInfo Firmware = new("16Q4EMS2.107", "05132019");

    private readonly FakeEcRegisters _ec = P65Memory.Faz0Snapshot();
    private readonly ListLog _log = new();
    private readonly List<TimeSpan> _sleeps = [];
    private readonly EcWorker _worker;

    public P65ControlTests() =>
        _worker = new EcWorker(_ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50));

    public void Dispose() => _worker.Dispose();

    private EcAccessRetry Retry => EcAccessRetry.Default with { Sleep = _sleeps.Add };

    private P65Control Control(WritePolicy? policy = null, FirmwareInfo? firmware = null) =>
        new(_worker, new WriteAccessSetup(firmware ?? Firmware, new EcGateway(_worker, policy ?? Live, _log, retry: Retry)), _log, Retry);

    // --- access ---

    [Fact]
    public void Open_gateway_means_writes_enabled()
    {
        Control().Access.Should().Be(new DeviceAccess(Firmware, WriteMode.Enabled, LockReason: null));
    }

    [Fact]
    public void Dry_run_gateway_means_dry_run()
    {
        Control(Live with { DryRun = true }).Access.WriteMode.Should().Be(WriteMode.DryRun);
    }

    [Fact]
    public void Locked_gateway_reports_its_reason()
    {
        var access = Control(Live with { FirmwareSupported = false }).Access;

        access.WriteMode.Should().Be(WriteMode.Locked);
        access.LockReason.Should().Contain("firmware");
    }

    [Fact]
    public async Task A_failed_write_turns_access_to_locked()
    {
        _ec.StuckRegisters.Add(0xF2);
        var control = Control();

        var outcome = await control.SetPerformanceAsync(PerformanceMode.Balanced);

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        control.Access.WriteMode.Should().Be(WriteMode.Locked);
    }

    // --- reads ---

    [Fact]
    public async Task Sensor_reads_are_not_retried_so_a_silent_poll_is_skipped()
    {
        _ec.SilentAccesses = 1;

        var act = () => Control().ReadSensorsAsync();

        await act.Should().ThrowAsync<EcAccessException>();
        _sleeps.Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_sensors()
    {
        (await Control().ReadSensorsAsync()).CpuTempC.Should().Be(60);
    }

    [Fact]
    public async Task Control_state_reads_ride_out_a_silent_period()
    {
        _ec.SilentAccesses = 2;

        var state = await Control().ReadControlStateAsync();

        state.Performance.Should().Be(PerformanceMode.High);
        _sleeps.Should().HaveCount(2);
    }

    // --- writes ---

    [Fact]
    public async Task Applies_a_fan_profile()
    {
        var outcome = await Control().ApplyFanProfileAsync(Presets.Cool);

        outcome.Status.Should().Be(WriteStatus.Applied);
        new P65Device(_ec).ReadFanCurves().Should().Be(Presets.Cool.Curves);
    }

    [Theory]
    [InlineData(true, 0x82)]
    [InlineData(false, 0x02)]
    public async Task Cooler_boost_keeps_the_other_bits(bool on, byte expected)
    {
        _ec[0x98] = on ? (byte)0x02 : (byte)0x82;

        var outcome = await Control().SetCoolerBoostAsync(on);

        outcome.Status.Should().Be(WriteStatus.Applied);
        _ec[0x98].Should().Be(expected);
    }

    [Fact]
    public async Task Sets_charge_limit_and_fan_mode()
    {
        var control = Control();

        (await control.SetChargeLimitAsync(60)).Status.Should().Be(WriteStatus.Applied);
        (await control.SetFanModeAsync(FanMode.Auto)).Status.Should().Be(WriteStatus.Applied);

        _ec[0xEF].Should().Be(0xBC);
        _ec[0xF4].Should().Be(0x0D);
    }

    [Fact]
    public async Task Dry_run_writes_nothing()
    {
        var outcome = await Control(Live with { DryRun = true }).ApplyFanProfileAsync(Presets.Cool);

        outcome.Status.Should().Be(WriteStatus.DryRun);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Locked_writes_are_rejected()
    {
        var outcome = await Control(Live with { PreStateSaved = false }).SetCoolerBoostAsync(true);

        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Cooler_boost_is_rejected_when_the_register_cannot_be_read()
    {
        _ec.AccessError = new EcAccessException("EC hung");

        var outcome = await Control().SetCoolerBoostAsync(true);

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("EC hung");
    }

    // --- desired state ---

    [Fact]
    public async Task Applies_the_whole_desired_state_in_order()
    {
        var desired = new DesiredState("Cool", PerformanceMode.Balanced, 60);

        var outcomes = await Control().ApplyDesiredAsync(desired, ProfileCatalog.BuiltIn);

        outcomes.Should().HaveCount(3).And.OnlyContain(o => o.Status == WriteStatus.Applied);
        var state = new P65Device(_ec).ReadControlState();
        state.Should().BeEquivalentTo(new { FanCurves = Presets.Cool.Curves, Performance = PerformanceMode.Balanced, ChargeLimitPercent = 60 });
    }

    [Fact]
    public async Task Applying_the_desired_state_stops_at_the_first_failure()
    {
        _ec.StuckRegisters.Add(0xF2);
        var desired = new DesiredState("Cool", PerformanceMode.Balanced, 60);

        var outcomes = await Control().ApplyDesiredAsync(desired, ProfileCatalog.BuiltIn);

        outcomes.Select(o => o.Status).Should().Equal(WriteStatus.Applied, WriteStatus.FailedRecovered);
        _ec.Writes.Should().NotContain(w => w.Register == 0xEF);
    }

    [Fact]
    public async Task A_rejected_fan_table_stops_the_rest_of_the_desired_state()
    {
        var unsafeCurve = FanCurve.Of((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (88, 150));
        var catalog = new ProfileCatalog([new FanProfile("Bad", new FanCurves(unsafeCurve, unsafeCurve))]);

        var outcomes = await Control().ApplyDesiredAsync(new DesiredState("Bad", PerformanceMode.Balanced, FanMode: FanMode.Auto), catalog);

        outcomes.Select(o => o.Status).Should().Equal(WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
    }
}
