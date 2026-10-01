using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public sealed class P65ControlTests : IDisposable
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);
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
        Control().Access.Should().Be(new DeviceAccess(Firmware, WriteMode.Enabled, LockReason: null, PortFeaturesAvailable: true));
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

        var state = await Control().ReadControlStateAsync(PortUse.Allowed);

        state.Performance.Should().Be(PerformanceMode.High);
        _sleeps.Should().HaveCount(2);
    }

    // --- port state (Cooler Boost, charge limit) ---

    private List<byte> RecordPortReads()
    {
        var reads = new List<byte>();
        _ec.ReadHook = register =>
        {
            if (register is 0x98 or 0xEF)
            {
                reads.Add(register);
            }

            return null;
        };
        return reads;
    }

    [Fact]
    public async Task The_port_state_is_read_once_and_then_served_from_the_cache()
    {
        var portReads = RecordPortReads();
        var control = Control();

        var first = await control.ReadControlStateAsync(PortUse.Allowed);
        _ec[0xEF] = 0xBC; // changed behind M-Tool's back: the cache does not see it
        var second = await control.ReadControlStateAsync(PortUse.Allowed);

        first.Port.Should().Be(new PortState(0x02, 0xD0));
        second.Port.Should().Be(first.Port);
        portReads.Should().BeEquivalentTo([0x98, 0xEF]);
    }

    [Fact]
    public async Task Successful_port_writes_update_the_cache_without_reading_the_port_again()
    {
        var control = Control();
        await control.ReadControlStateAsync(PortUse.Allowed);

        await control.SetChargeLimitAsync(60);
        var portReads = RecordPortReads();
        var state = await control.ReadControlStateAsync(PortUse.Allowed);

        state.Port!.ChargeLimitPercent.Should().Be(60);
        portReads.Should().BeEmpty();
    }

    [Fact]
    public async Task Cooler_boost_reads_the_port_state_first_and_caches_the_result()
    {
        var control = Control();

        (await control.SetCoolerBoostAsync(true)).Status.Should().Be(WriteStatus.Applied);
        var portReads = RecordPortReads();
        var state = await control.ReadControlStateAsync(PortUse.Allowed);

        state.Port.Should().Be(new PortState(0x82, 0xD0));
        portReads.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_port_write_makes_the_port_state_unknown()
    {
        var control = Control();
        await control.ReadControlStateAsync(PortUse.Allowed);
        _ec.StuckRegisters.Add(0xEF);

        await control.SetChargeLimitAsync(60);
        var portReads = RecordPortReads();
        var state = await control.ReadControlStateAsync(PortUse.Allowed);

        state.Port.Should().BeNull();
        portReads.Should().BeEmpty(); // no automatic port read after a failed port write
    }

    [Fact]
    public async Task A_failed_first_read_is_tried_again_on_the_next_refresh()
    {
        var silent = true;
        _ec.ReadHook = register => silent && register == 0x98 ? throw new EcAccessException("port silent") : null;
        var control = Control();

        var first = await control.ReadControlStateAsync(PortUse.Allowed);
        silent = false;
        var second = await control.ReadControlStateAsync(PortUse.Allowed);

        first.Port.Should().BeNull();
        first.Performance.Should().Be(PerformanceMode.High);
        second.Port.Should().Be(new PortState(0x02, 0xD0));
    }

    [Fact]
    public async Task A_failed_cooler_boost_read_makes_the_cached_state_unknown()
    {
        var control = Control();
        await control.ReadControlStateAsync(PortUse.Allowed);
        _ec.ReadHook = register => register == 0x98 ? throw new EcAccessException("port silent") : null;

        var outcome = await control.SetCoolerBoostAsync(true);
        var state = await control.ReadControlStateAsync(PortUse.Allowed);

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("port silent");
        state.Port.Should().BeNull();
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Locked_writes_never_read_the_port_for_a_write()
    {
        var control = Control(Live with { PreStateSaved = false });
        var portReads = RecordPortReads();

        var boost = await control.SetCoolerBoostAsync(true);
        var outcomes = await control.ApplyDesiredAsync(new DesiredState("Default", ChargeLimitPercent: 60), ProfileCatalog.BuiltIn);

        boost.Status.Should().Be(WriteStatus.Rejected);
        outcomes.Should().OnlyContain(o => o.Status == WriteStatus.Rejected);
        portReads.Should().BeEmpty();
    }


    [Fact]
    public async Task Without_a_port_nothing_reaches_the_port_registers()
    {
        var portReads = RecordPortReads();
        var control = Control(Live with { PortAvailable = false });

        var state = await control.ReadControlStateAsync(PortUse.Allowed);
        var boost = await control.SetCoolerBoostAsync(true);
        var charge = await control.SetChargeLimitAsync(60);

        control.Access.PortFeaturesAvailable.Should().BeFalse();
        state.Port.Should().BeNull();
        state.Performance.Should().Be(PerformanceMode.High);
        boost.Status.Should().Be(WriteStatus.Rejected);
        boost.Message.Should().Contain("port");
        charge.Status.Should().Be(WriteStatus.Rejected);
        portReads.Should().BeEmpty();
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Reapplying_without_the_port_parts_never_touches_the_port()
    {
        var portReads = RecordPortReads();
        var control = Control(); // nothing cached yet, as right after start-up or resume
        var desired = new DesiredState("Cool", PerformanceMode.Balanced, ChargeLimitPercent: 60);

        var outcomes = await control.ApplyDesiredAsync(desired.WithoutPortParts(), ProfileCatalog.BuiltIn);

        outcomes.Should().HaveCount(2).And.OnlyContain(o => o.Status == WriteStatus.Applied);
        portReads.Should().BeEmpty();
        _ec.Writes.Should().NotContain(w => w.Register == 0x98 || w.Register == 0xEF);
    }

    [Fact]
    public async Task A_state_read_without_port_access_serves_a_known_cache()
    {
        var control = Control();
        await control.ReadControlStateAsync(PortUse.Allowed);
        _ec[0xEF] = 0xBC;
        var portReads = RecordPortReads();

        var state = await control.ReadControlStateAsync(PortUse.None);

        state.Port.Should().Be(new PortState(0x02, 0xD0));
        portReads.Should().BeEmpty();
    }

    [Fact]
    public async Task A_state_read_without_port_access_leaves_an_unread_port_unknown_for_the_next_read()
    {
        var portReads = RecordPortReads();
        var control = Control();

        var withoutPort = await control.ReadControlStateAsync(PortUse.None);
        portReads.Should().BeEmpty();
        var withPort = await control.ReadControlStateAsync(PortUse.Allowed);

        withoutPort.Port.Should().BeNull();
        withoutPort.Performance.Should().Be(PerformanceMode.High);
        withPort.Port.Should().Be(new PortState(0x02, 0xD0));
        portReads.Should().BeEquivalentTo([0x98, 0xEF]);
    }

    [Fact]
    public async Task A_state_read_without_port_access_after_a_failed_read_stays_unknown_until_a_read_may_use_the_port()
    {
        var silent = true;
        _ec.ReadHook = register => silent && register == 0x98 ? throw new EcAccessException("port silent") : null;
        var control = Control();
        await control.ReadControlStateAsync(PortUse.Allowed); // fails: unknown, not settled
        silent = false;
        var portReads = RecordPortReads();

        var withoutPort = await control.ReadControlStateAsync(PortUse.None);
        portReads.Should().BeEmpty();
        var withPort = await control.ReadControlStateAsync(PortUse.Allowed);

        withoutPort.Port.Should().BeNull();
        withPort.Port.Should().Be(new PortState(0x02, 0xD0));
    }

    [Fact]
    public async Task A_state_read_without_port_access_in_a_wmi_only_session_is_unknown()
    {
        var portReads = RecordPortReads();

        var state = await Control(Live with { PortAvailable = false }).ReadControlStateAsync(PortUse.None);

        state.Port.Should().BeNull();
        portReads.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_port_read_is_logged()
    {
        var control = Control();

        await control.ReadControlStateAsync(PortUse.Allowed);
        await control.ReadControlStateAsync(PortUse.Allowed); // cache: no second line

        _log.Lines.Should().ContainSingle(l => l.StartsWith("INFO") && l.Contains("port okundu") && l.Contains("0x98=0x02 0xEF=0xD0"));
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
        var device = new P65Device(_ec);
        device.ReadControlState().Should().BeEquivalentTo(new { FanCurves = Presets.Cool.Curves, Performance = PerformanceMode.Balanced });
        device.ReadPortState().ChargeLimitPercent.Should().Be(60);
    }

    [Fact]
    public async Task The_charge_limit_is_written_last_after_the_wmi_parts()
    {
        var desired = new DesiredState("Cool", PerformanceMode.Balanced, 60, FanMode.Auto);

        await Control().ApplyDesiredAsync(desired, ProfileCatalog.BuiltIn);

        _ec.Writes.Last().Register.Should().Be(0xEF);
        _ec.Writes.Select(w => w.Register).Should().ContainInOrder(0xF2, 0xF4, 0xEF);
    }

    [Fact]
    public async Task A_charge_limit_the_ec_already_holds_is_not_written_again()
    {
        var desired = new DesiredState("Cool", ChargeLimitPercent: 80); // 0xEF is 0xD0 = 80 %

        var outcomes = await Control().ApplyDesiredAsync(desired, ProfileCatalog.BuiltIn);

        outcomes.Should().ContainSingle();
        _ec.Writes.Should().NotContain(w => w.Register == 0xEF);
    }

    [Fact]
    public async Task Reapplying_reads_the_charge_limit_fresh_because_the_ec_may_have_reset_it()
    {
        var control = Control();
        await control.ReadControlStateAsync(PortUse.Allowed); // caches 80 %
        _ec[0xEF] = 0x64; // reset behind M-Tool's back (limit off)

        var outcomes = await control.ApplyDesiredAsync(new DesiredState("Default", ChargeLimitPercent: 80), ProfileCatalog.BuiltIn);

        outcomes.Should().HaveCount(2).And.OnlyContain(o => o.Status == WriteStatus.Applied);
        _ec[0xEF].Should().Be(0xD0);
    }

    [Fact]
    public async Task An_unreadable_charge_limit_is_not_written()
    {
        _ec.ReadHook = register => register == 0xEF ? throw new EcAccessException("port silent") : null;

        var outcomes = await Control().ApplyDesiredAsync(new DesiredState("Default", ChargeLimitPercent: 60), ProfileCatalog.BuiltIn);

        outcomes.Select(o => o.Status).Should().Equal(WriteStatus.Applied, WriteStatus.Rejected);
        _ec.Writes.Should().NotContain(w => w.Register == 0xEF);
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
