using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Settings;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Ec;

/// <summary>
/// The WMI2 part of the gateway (plan asama15 15c-3): one <c>Set_Data</c> per change, checked against a read
/// taken right before it, verified under its mask, and on failure the old value back and the session locked.
/// </summary>
public sealed class EcGatewayWmi2Tests : IDisposable
{
    private const string Standard = "15M3EMS1.105";
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.Load(
        """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105", "15M1IMS2.105"],
          "families": ["15M3EMS1.1", "15M1IMS2.1"],
          "familyOverrides": { "15M1IMS2.1": { "eco": "0xC6" } }
        }
        """,
        [P65()]);

    private static readonly Wmi2LayoutResult Verified = Wmi2LayoutCheck.Check(new Wmi2Status(0x02, 0xC1, 0x0D, 0xE4));

    private readonly SimulatedWmi2Ec _ec = SimulatedWmi2Ec.Typical();
    private readonly FakeEcLock _lock = new();
    private readonly ListLog _log = new();
    private readonly EcWorker _worker;
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-wmi2-gateway-").FullName;
    private bool _wmi2Only = true;

    public EcGatewayWmi2Tests() =>
        _worker = new EcWorker(new Wmi2WritableRegisters(_ec, _ec), _lock, TimeSpan.FromMilliseconds(50));

    public void Dispose()
    {
        _worker.Dispose();
        _ec.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private PreStateStore Store => PreStateStore.ForWmi2(_folder);

    private Wmi2GatewaySetup Setup(string? firmware = Standard) =>
        new(List, firmware is null ? null : new FirmwareInfo(firmware, ""), Consented: true, Layout: Verified,
            PreState: Store, IsWmi2Only: () => _wmi2Only, DryRun: false, Now: () => Now);

    private EcGateway Gateway(Wmi2GatewaySetup? setup = null) => new(_worker, setup ?? Setup(), _log);

    private async Task<EcGateway> TestedGateway(Wmi2GatewaySetup? setup = null)
    {
        var gateway = Gateway(setup);
        (await gateway.TestWmi2WritePathAsync()).Status.Should().Be(WriteStatus.Applied);
        return gateway;
    }

    private IEnumerable<Wmi2WriteRequest> WritesAfterTest => _ec.Writes.Skip(1);

    // --- happy path ---

    [Fact]
    public async Task The_write_path_test_writes_the_current_performance_byte_again()
    {
        var gateway = Gateway();

        var outcome = await gateway.TestWmi2WritePathAsync();

        outcome.Should().Be(new Wmi2WriteOutcome(WriteStatus.Applied, 0xD2, 0xC1, new RegisterWrite(0xD2, 0xC1)));
        _ec.Writes.Should().ContainSingle().Which.Should().Be(Wmi2Writes.Request(0xD2, 0xC1));
        gateway.IsWmi2WritePathTested.Should().BeTrue();
    }

    [Fact]
    public async Task A_change_is_one_set_data_call_verified_by_a_read_back()
    {
        var gateway = await TestedGateway();

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Should().Be(new Wmi2WriteOutcome(WriteStatus.Applied, 0xD2, 0xC1, new RegisterWrite(0xD2, 0xC4)));
        WritesAfterTest.Should().Equal(Wmi2Writes.Request(0xD2, 0xC4));
        _ec[0xD2].Should().Be(0xC4);
        _ec.Reads.Last().Should().Be(Wmi2Calls.DataRequest(0xD2), "the write is read back");
        _log.Lines.Should().Contain("INFO WRITE-WMI2 0xD2: 0xC1 -> 0xC4 (experimental, 15M3EMS1.105)");
    }

    [Fact]
    public async Task Only_ceiling_registers_are_ever_read_or_written()
    {
        var gateway = await TestedGateway();

        await gateway.ApplyWmi2Async(new Wmi2Change.CoolerBoost(true));
        await gateway.ApplyWmi2Async(new Wmi2Change.FanMode(Wmi2FanMode.Silent));
        await gateway.ApplyWmi2Async(new Wmi2Change.ChargeLimit(60));
        await gateway.RevertWmi2Async();

        _ec.Reads.Should().OnlyContain(r => r.Method == Wmi2Calls.GetData && Wmi2WriteCeiling.Registers.Contains(r.Sub));
        _ec.Writes.Should().OnlyContain(w => Wmi2WriteCeiling.Registers.Contains(w.Register));
    }

    // --- policy ---

    [Fact]
    public async Task A_change_before_the_write_path_test_is_refused()
    {
        var outcome = await Gateway().ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Block.Should().Be(Wmi2WriteBlock.WritePathNotTested);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Dry_run_never_calls_set_data()
    {
        var gateway = Gateway(Setup() with { DryRun = true });

        var test = await gateway.TestWmi2WritePathAsync();
        var change = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        test.Status.Should().Be(WriteStatus.DryRun);
        change.Should().Be(new Wmi2WriteOutcome(WriteStatus.DryRun, 0xD2, 0xC1, new RegisterWrite(0xD2, 0xC4)));
        _ec.Writes.Should().BeEmpty();
        gateway.IsWmi2WritePathTested.Should().BeFalse("nothing was written, so nothing was tested");
        _log.Lines.Should().Contain(l => l.Contains("DRY-RUN WMI2 0xD2: 0xC1 -> 0xC4"));
    }

    public static TheoryData<string, Wmi2WriteBlock> ClosedSetups => new()
    {
        { "no consent", Wmi2WriteBlock.NoConsent },
        { "layout", Wmi2WriteBlock.LayoutNotVerified },
        { "family only", Wmi2WriteBlock.NotExactFirmware },
        { "no firmware", Wmi2WriteBlock.NotExactFirmware },
    };

    [Theory]
    [MemberData(nameof(ClosedSetups))]
    public async Task A_closed_condition_refuses_before_any_ec_access(string closed, Wmi2WriteBlock block)
    {
        var setup = closed switch
        {
            "no consent" => Setup() with { Consented = false },
            "layout" => Setup() with { Layout = Wmi2LayoutCheck.Check(new Wmi2Status(0x02, 0x00, 0x0D, 0xE4)) },
            "family only" => Setup("15M3EMS1.199"),
            _ => Setup(firmware: null),
        };

        var outcome = await Gateway(setup).TestWmi2WritePathAsync();

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Block.Should().Be(block);
        _ec.Reads.Should().BeEmpty();
        _ec.Writes.Should().BeEmpty();
        Store.Exists.Should().BeFalse("no snapshot is taken for a write that cannot happen");
    }

    [Fact]
    public async Task The_wmi2_only_probe_is_asked_right_before_the_write()
    {
        var gateway = await TestedGateway();
        _wmi2Only = false;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Block.Should().Be(Wmi2WriteBlock.NotWmi2Only);
        WritesAfterTest.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_wmi2_only_probe_counts_as_a_wmi1_laptop()
    {
        var gateway = Gateway(Setup() with { IsWmi2Only = () => throw new InvalidOperationException("WMI down") });

        var outcome = await gateway.TestWmi2WritePathAsync();

        outcome.Block.Should().Be(Wmi2WriteBlock.NotWmi2Only);
        _ec.Writes.Should().BeEmpty();
    }

    // --- rules, against a read taken right before ---

    [Fact]
    public async Task A_current_byte_outside_the_layout_refuses_the_write()
    {
        var gateway = await TestedGateway();
        _ec[0xD2] = 0x00;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Should().Be(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, 0x00, Rejection: Wmi2Rejection.CurrentOutsideLayout));
        WritesAfterTest.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Wmi2FanMode.Advanced, Wmi2Rejection.AdvancedFanMode)]
    public async Task The_advanced_fan_mode_is_never_written(Wmi2FanMode mode, Wmi2Rejection rejection)
    {
        var gateway = await TestedGateway();

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.FanMode(mode));

        outcome.Rejection.Should().Be(rejection);
        WritesAfterTest.Should().BeEmpty();
    }

    [Fact]
    public async Task A_charge_limit_under_the_ceiling_is_refused()
    {
        var gateway = await TestedGateway();

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.ChargeLimit(40));

        outcome.Rejection.Should().Be(Wmi2Rejection.ValueNotAllowed);
        WritesAfterTest.Should().BeEmpty();
    }

    // --- the snapshot ---

    [Fact]
    public async Task The_snapshot_is_taken_before_the_first_write()
    {
        await Gateway().TestWmi2WritePathAsync();

        var saved = Store.Load();
        saved.Should().NotBeNull();
        saved!.FirmwareVersion.Should().Be(Standard);
        saved.Registers.Should().Equal(
            new RegisterValue(0x98, 0x02), new RegisterValue(0xD2, 0xC1), new RegisterValue(0xD4, 0x0D), new RegisterValue(0xD7, 0xD0));
    }

    [Fact]
    public async Task A_snapshot_that_cannot_be_taken_refuses_the_write()
    {
        var flip = false;
        _ec.ReadAnswer = r => r.Sub == 0xD2 && (flip = !flip) ? FakeWmi2Methods.Packet(0xC4) : null;

        var outcome = await Gateway().TestWmi2WritePathAsync();

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Problem.Should().Be(Wmi2GatewayProblem.PreStateNotTaken);
        _ec.Writes.Should().BeEmpty();
        Store.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task An_existing_snapshot_is_kept()
    {
        Store.SaveIfMissing(new PreMToolState(Standard, "", Now.AddDays(-1), [
            new(0x98, 0x00), new(0xD2, 0xC2), new(0xD4, 0x1D), new(0xD7, 0xE4)]));
        var before = File.ReadAllText(Path.Combine(_folder, "pre-mtool-state-wmi2.json"));

        await Gateway().TestWmi2WritePathAsync();

        File.ReadAllText(Path.Combine(_folder, "pre-mtool-state-wmi2.json")).Should().Be(before);
    }

    // --- failure ---

    [Fact]
    public async Task A_write_that_never_reads_back_locks_the_session_after_three_attempts()
    {
        var gateway = await TestedGateway();
        _ec.IgnoresWrites = true;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));
        var next = await gateway.ApplyWmi2Async(new Wmi2Change.ChargeLimit(60));

        outcome.Should().Be(new Wmi2WriteOutcome(
            WriteStatus.FailedRecovered, 0xD2, 0xC1, new RegisterWrite(0xD2, 0xC4), Problem: Wmi2GatewayProblem.ReadBackDiffers));
        WritesAfterTest.Should().Equal(Enumerable.Repeat(Wmi2Writes.Request(0xD2, 0xC4), 3));
        next.Block.Should().Be(Wmi2WriteBlock.SessionLocked);
        gateway.Wmi2SessionLockReason.Should().Contain("0xD2");
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR", StringComparison.Ordinal) && l.Contains("MSI Center"));
        Directory.GetFiles(_folder).Select(Path.GetFileName).Should().Equal("pre-mtool-state-wmi2.json");
    }

    [Fact]
    public async Task A_write_that_lands_elsewhere_fails_and_locks()
    {
        var gateway = await TestedGateway();
        _ec.WritesNeighbour = true;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        outcome.Problem.Should().Be(Wmi2GatewayProblem.ReadBackDiffers);
        gateway.Wmi2SessionLockReason.Should().NotBeNull();
    }

    [Fact]
    public async Task A_failed_write_call_puts_the_old_value_back()
    {
        var gateway = await TestedGateway();
        _ec.WriteAnswer = w => w.Value == 0xC4 ? FakeWmi2Methods.Packet(0, status: 0) : null; // it wrote, but answered a failure

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        outcome.Problem.Should().Be(Wmi2GatewayProblem.WriteFailed);
        WritesAfterTest.Should().Equal(Wmi2Writes.Request(0xD2, 0xC4), Wmi2Writes.Request(0xD2, 0xC1));
        _ec[0xD2].Should().Be(0xC1);
    }

    [Fact]
    public async Task An_ec_that_stops_answering_after_a_write_is_unrecovered()
    {
        var gateway = await TestedGateway();
        _ec.WriteAnswer = _ =>
        {
            _ec.Error = new EcAccessException("gone");
            return null;
        };

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Status.Should().Be(WriteStatus.FailedUnrecovered);
        gateway.Wmi2SessionLockReason.Should().NotBeNull();
    }

    [Fact]
    public async Task An_unreachable_ec_refuses_without_writing()
    {
        var gateway = await TestedGateway();
        _lock.Available = false;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Problem.Should().Be(Wmi2GatewayProblem.Unreachable);
        gateway.Wmi2SessionLockReason.Should().BeNull("nothing was written");
    }

    [Fact]
    public async Task A_register_that_leaves_the_layout_between_attempts_is_not_written_again_or_back()
    {
        var gateway = await TestedGateway();
        _ec.IgnoresWrites = true;
        var written = false;
        _ec.WriteAnswer = _ =>
        {
            written = true;
            return null;
        };
        _ec.ReadAnswer = r => written && r.Sub == 0xD2 ? FakeWmi2Methods.Packet(0x00) : null;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));

        outcome.Status.Should().Be(WriteStatus.FailedUnrecovered, "0x00 is outside the layout, so C1 is not written over it either");
        outcome.Problem.Should().Be(Wmi2GatewayProblem.RegisterMoved);
        WritesAfterTest.Should().Equal(Wmi2Writes.Request(0xD2, 0xC4));
        _log.Lines.Should().Contain(l => l.Contains("CurrentOutsideLayout"));
    }

    [Fact]
    public async Task A_revert_that_shows_up_late_counts_as_written()
    {
        var gateway = await TestedGateway();
        await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));
        var lateReads = 0;
        _ec.WriteAnswer = _ =>
        {
            lateReads = 1;
            return null;
        };
        _ec.ReadAnswer = r => r.Sub == 0xD2 && lateReads-- == 1 ? FakeWmi2Methods.Packet(0xC4) : null; // the read-back is one read behind

        var outcomes = await gateway.RevertWmi2Async();

        outcomes.Single(o => o.Register == 0xD2).Status.Should().Be(WriteStatus.Applied);
        WritesAfterTest.Should().Equal(Wmi2Writes.Request(0xD2, 0xC4), Wmi2Writes.Request(0xD2, 0xC1));
    }

    [Fact]
    public async Task The_write_path_test_is_refused_over_a_byte_outside_the_layout()
    {
        _ec[0xD2] = 0x00;

        var outcome = await Gateway().TestWmi2WritePathAsync();

        outcome.Rejection.Should().Be(Wmi2Rejection.CurrentOutsideLayout);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_clock_the_snapshot_takes_the_time_now()
    {
        var start = DateTimeOffset.Now;

        await Gateway(Setup() with { Now = null }).TestWmi2WritePathAsync();

        Store.Load()!.CapturedAt.Should().BeOnOrAfter(start).And.BeOnOrBefore(DateTimeOffset.Now);
    }

    [Fact]
    public async Task An_unreadable_snapshot_refuses_the_revert()
    {
        var gateway = await TestedGateway();
        using var held = new FileStream(Path.Combine(_folder, "pre-mtool-state-wmi2.json"), FileMode.Open, FileAccess.Read, FileShare.None);

        var outcomes = await gateway.RevertWmi2Async();

        outcomes.Should().OnlyContain(o => o.Block == Wmi2WriteBlock.NoPreState);
        WritesAfterTest.Should().BeEmpty();
    }

    [Fact]
    public async Task A_change_the_register_already_holds_is_not_written()
    {
        var gateway = await TestedGateway();

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.CoolerBoost(false)); // 0x02: bit 7 is already off

        outcome.Should().Be(new Wmi2WriteOutcome(WriteStatus.Applied, 0x98, 0x02));
        WritesAfterTest.Should().BeEmpty();
    }

    [Fact]
    public async Task A_laptop_that_is_not_wmi2_only_is_neither_read_nor_given_a_snapshot()
    {
        _wmi2Only = false;

        var outcome = await Gateway().TestWmi2WritePathAsync();

        outcome.Block.Should().Be(Wmi2WriteBlock.NotWmi2Only);
        _ec.Reads.Should().BeEmpty();
        Store.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task A_snapshot_of_another_firmware_refuses_and_is_kept()
    {
        Store.SaveIfMissing(new PreMToolState("15M3EMS1.104", "", Now.AddDays(-30), [
            new(0x98, 0x02), new(0xD2, 0xC1), new(0xD4, 0x0D), new(0xD7, 0xD0)]));
        var file = Path.Combine(_folder, "pre-mtool-state-wmi2.json");
        var before = File.ReadAllText(file);

        var outcome = await Gateway().TestWmi2WritePathAsync();

        outcome.Should().Be(new Wmi2WriteOutcome(
            WriteStatus.Rejected, 0xD2, Block: Wmi2WriteBlock.NoPreState, Problem: Wmi2GatewayProblem.PreStateOtherFirmware));
        _ec.Writes.Should().BeEmpty();
        File.ReadAllText(file).Should().Be(before);
    }

    [Fact]
    public async Task An_unreadable_snapshot_refuses_and_is_kept()
    {
        File.WriteAllText(Path.Combine(_folder, "pre-mtool-state-wmi2.json"), "{ not json");

        var outcome = await Gateway().TestWmi2WritePathAsync();

        outcome.Problem.Should().Be(Wmi2GatewayProblem.PreStateUnreadable);
        _ec.Writes.Should().BeEmpty();
        File.ReadAllText(Path.Combine(_folder, "pre-mtool-state-wmi2.json")).Should().Be("{ not json");
    }

    [Fact]
    public async Task A_failed_write_path_test_is_not_a_passed_one()
    {
        var gateway = Gateway();
        _ec.WriteAnswer = _ => FakeWmi2Methods.Packet(0, status: 0);

        var outcome = await gateway.TestWmi2WritePathAsync();

        outcome.Problem.Should().Be(Wmi2GatewayProblem.WriteFailed);
        gateway.IsWmi2WritePathTested.Should().BeFalse();
        gateway.Wmi2SessionLockReason.Should().NotBeNull();
    }

    [Fact]
    public async Task After_a_failure_the_revert_is_refused_too()
    {
        var gateway = await TestedGateway();
        _ec.IgnoresWrites = true;
        await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));
        var writes = _ec.Writes.Count;

        var outcomes = await gateway.RevertWmi2Async();

        outcomes.Should().OnlyContain(o => o.Block == Wmi2WriteBlock.SessionLocked);
        _ec.Writes.Should().HaveCount(writes);
    }

    [Fact]
    public async Task Revert_in_a_new_session_waits_for_the_write_path_test()
    {
        await (await TestedGateway()).ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));
        var nextSession = Gateway();

        var outcomes = await nextSession.RevertWmi2Async();

        outcomes.Should().OnlyContain(o => o.Block == Wmi2WriteBlock.WritePathNotTested);
        _ec[0xD2].Should().Be(0xC4);
    }

    [Fact]
    public async Task A_cancelled_revert_reports_every_register_as_not_attempted()
    {
        var gateway = await TestedGateway();
        await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var outcomes = await gateway.RevertWmi2Async(cancelled.Token);

        outcomes.Select(o => o.Problem).Should().Equal(Enumerable.Repeat<Wmi2GatewayProblem?>(Wmi2GatewayProblem.Cancelled, 4));
        _ec[0xD2].Should().Be(0xC4);
    }

    [Fact]
    public async Task A_cancelled_change_writes_nothing()
    {
        var gateway = await TestedGateway();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var change = () => gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High), cancelled.Token);

        await change.Should().ThrowAsync<OperationCanceledException>();
        WritesAfterTest.Should().BeEmpty();
    }

    // --- a charge limit under 50 % (Kutay, 2026-10-26: no trip to MSI Center) ---

    [Fact]
    public async Task A_new_charge_limit_under_50_is_still_refused()
    {
        var gateway = await TestedGateway();

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.ChargeLimit(30));

        outcome.Rejection.Should().Be(Wmi2Rejection.ValueNotAllowed);
        WritesAfterTest.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_charge_write_puts_a_limit_under_50_back()
    {
        _ec[0xD7] = 0x9E; // 30 %, set by MSI Center
        var gateway = await TestedGateway();
        _ec.WriteAnswer = w => w.Value == 0xBC ? FakeWmi2Methods.Packet(0, status: 0) : null;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.ChargeLimit(60));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        WritesAfterTest.Should().Equal(Wmi2Writes.Request(0xD7, 0xBC), Wmi2Writes.Request(0xD7, 0x9E));
        _ec[0xD7].Should().Be(0x9E);
    }

    [Fact]
    public async Task Revert_puts_a_saved_limit_under_50_back()
    {
        _ec[0xD7] = 0x9E;
        var gateway = await TestedGateway();
        await gateway.ApplyWmi2Async(new Wmi2Change.ChargeLimit(80));

        var outcomes = await gateway.RevertWmi2Async();

        outcomes.Single(o => o.Register == 0xD7).Write.Should().Be(new RegisterWrite(0xD7, 0x9E));
        _ec[0xD7].Should().Be(0x9E);
    }

    // --- Cooler Boost: the low bits change by themselves ---

    [Fact]
    public async Task Cooler_boost_keeps_the_low_bits_of_the_read_right_before()
    {
        var gateway = await TestedGateway();
        _ec.CoolerBoostNoise = true;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.CoolerBoost(true));

        outcome.Status.Should().Be(WriteStatus.Applied);
        var read = outcome.Before!.Value;
        WritesAfterTest.Should().Equal(Wmi2Writes.Request(0x98, (byte)(read | 0x80)));
        read.Should().NotBe(0x02, "the snapshot's reads moved the low bits on");
        _log.Lines.Should().NotContain(l => l.Contains("read back"), "only bit 7 is compared, so the first attempt holds");
    }

    [Fact]
    public async Task Every_cooler_boost_attempt_is_planned_from_a_fresh_read()
    {
        var gateway = await TestedGateway();
        _ec.CoolerBoostNoise = true;
        _ec.IgnoresWrites = true;

        var outcome = await gateway.ApplyWmi2Async(new Wmi2Change.CoolerBoost(true));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        var attempts = WritesAfterTest.ToArray();
        attempts.Should().HaveCount(3).And.OnlyContain(w => w.Register == 0x98 && (w.Value & 0x80) != 0);
        attempts.Select(w => w.Value).Should().OnlyHaveUniqueItems("a stale read would write the same low bits again");
    }

    // --- one gateway, one path ---

    [Fact]
    public async Task A_p65_gateway_refuses_a_wmi2_write_untouched()
    {
        var p65 = new EcGateway(_worker, new WritePolicy(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: false), _log);

        var outcome = await p65.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));
        var test = await p65.TestWmi2WritePathAsync();
        var revert = await p65.RevertWmi2Async();

        p65.Kind.Should().Be(EcGatewayKind.P65Wmi1);
        new[] { outcome, test }.Concat(revert).Should().OnlyContain(o => o.Problem == Wmi2GatewayProblem.WrongGateway);
        _ec.Reads.Should().BeEmpty();
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_wmi2_gateway_refuses_a_p65_plan_untouched()
    {
        var gateway = Gateway();

        var outcome = await gateway.ApplyAsync(new WritePlan("test", [new RegisterWrite(0xD2, 0xC4)]));

        gateway.Kind.Should().Be(EcGatewayKind.Wmi2Experimental);
        gateway.IsWriteEnabled.Should().BeFalse();
        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Reads.Should().BeEmpty();
        _ec.Writes.Should().BeEmpty();
    }

    // --- back to before M-Tool ---

    [Fact]
    public async Task Revert_writes_every_changed_register_back_one_by_one()
    {
        var gateway = await TestedGateway();
        await gateway.ApplyWmi2Async(new Wmi2Change.Performance(PerformanceMode.High));
        await gateway.ApplyWmi2Async(new Wmi2Change.FanMode(Wmi2FanMode.Silent));

        var outcomes = await gateway.RevertWmi2Async();

        outcomes.Select(o => (o.Register, o.Status)).Should().Equal(
            (0x98, WriteStatus.Applied), (0xD2, WriteStatus.Applied), (0xD4, WriteStatus.Applied), (0xD7, WriteStatus.Applied));
        outcomes.Where(o => o.Write is not null).Select(o => o.Register).Should().Equal(0xD2, 0xD4);
        (_ec[0xD2], _ec[0xD4]).Should().Be((0xC1, 0x0D));
    }

    [Fact]
    public async Task Revert_skips_a_saved_byte_the_rules_do_not_allow()
    {
        // C6 is eco only on 15M1IMS2: inside the type gate, outside this family's rules (decision A, 2026-10-26).
        Store.SaveIfMissing(new PreMToolState(Standard, "", Now.AddDays(-1), [
            new(0x98, 0x02), new(0xD2, 0xC6), new(0xD4, 0x1D), new(0xD7, 0xD0)]));
        var gateway = await TestedGateway();

        var outcomes = await gateway.RevertWmi2Async();

        outcomes.Single(o => o.Register == 0xD2).Should().Be(
            new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, 0xC1, Rejection: Wmi2Rejection.ValueNotAllowed));
        WritesAfterTest.Should().Equal(Wmi2Writes.Request(0xD4, 0x1D));
    }

    [Fact]
    public async Task Revert_keeps_the_current_low_bits_of_cooler_boost()
    {
        var gateway = await TestedGateway();
        await gateway.ApplyWmi2Async(new Wmi2Change.CoolerBoost(true));
        _ec[0x98] = 0x85;

        var outcomes = await gateway.RevertWmi2Async();

        outcomes[0].Write.Should().Be(new RegisterWrite(0x98, 0x05));
        _ec[0x98].Should().Be(0x05);
    }

    [Fact]
    public async Task Revert_without_a_snapshot_writes_nothing()
    {
        var gateway = Gateway(Setup() with { Consented = false });

        var outcomes = await gateway.RevertWmi2Async();

        outcomes.Should().OnlyContain(o => o.Status == WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
    }
}
