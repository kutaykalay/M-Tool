using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public sealed class EcGatewayTests : IDisposable
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);

    private readonly FakeEcRegisters _ec = P65Memory.FactorySnapshot();
    private readonly ListLog _log = new();
    private readonly EcWorker _worker;

    public EcGatewayTests() =>
        _worker = new EcWorker(_ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50));

    public void Dispose() => _worker.Dispose();

    private readonly List<string> _persistedLocks = [];
    private readonly List<TimeSpan> _sleeps = [];

    private EcGateway Gateway(WritePolicy? policy = null, IAppLog? log = null) =>
        new(_worker, policy ?? Live, log ?? _log, _persistedLocks.Add, EcAccessRetry.Default with { Sleep = _sleeps.Add });

    private static WritePlan Plan(params RegisterWrite[] writes) => new("test", writes);

    // --- gates ---

    [Fact]
    public async Task Unsupported_firmware_rejects_every_write()
    {
        var gateway = Gateway(Live with { FirmwareSupported = false });

        var outcome = await gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("firmware");
        _ec.Writes.Should().BeEmpty();
        gateway.IsWriteEnabled.Should().BeFalse();
        gateway.LockReason.Should().Contain("firmware");
    }

    [Fact]
    public async Task Missing_pre_state_backup_rejects_every_write()
    {
        var gateway = Gateway(Live with { PreStateSaved = false });

        var outcome = await gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
        gateway.LockReason.Should().Contain("yedeği");
    }

    [Fact]
    public void An_open_gateway_has_no_lock_reason()
    {
        Gateway().LockReason.Should().BeNull();
    }

    [Theory]
    [InlineData(0xF3, 0x83)] // keyboard light: readable, not writable in v1
    [InlineData(0x68, 0x10)] // CPU temperature sensor
    [InlineData(0x00, 0x00)]
    public async Task Registers_outside_the_whitelist_are_rejected(byte register, byte value)
    {
        var outcome = await Gateway().ApplyAsync(Plan(new RegisterWrite(register, value)));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain($"0x{register:X2}");
        _ec.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0xF2, 0xC4)] // turbo: unverified on this laptop
    [InlineData(0xF4, 0x1D)] // EC silent mode: not offered
    [InlineData(0xEF, 0x80 | 40)]
    [InlineData(0xEF, 80)] // enable bit missing
    public async Task Values_outside_the_register_rules_are_rejected(byte register, byte value)
    {
        var outcome = await Gateway().ApplyAsync(Plan(new RegisterWrite(register, value)));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Cooler_boost_write_may_not_touch_other_bits()
    {
        var outcome = await Gateway().ApplyAsync(Plan(new RegisterWrite(0x98, 0x80)));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("bit");
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_plan_that_would_leave_an_unsafe_fan_table_is_rejected()
    {
        // Last CPU step down to 70 %: violates the safety floor.
        var outcome = await Gateway().ApplyAsync(Plan(new RegisterWrite(0x78, 70)));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("CPU");
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_plan_is_rejected()
    {
        var outcome = await Gateway().ApplyAsync(Plan());

        outcome.Status.Should().Be(WriteStatus.Rejected);
    }

    [Fact]
    public async Task Duplicate_registers_in_one_plan_are_rejected()
    {
        var outcome = await Gateway().ApplyAsync(Plan(new RegisterWrite(0xF2, 0xC1), new RegisterWrite(0xF2, 0xC2)));

        outcome.Status.Should().Be(WriteStatus.Rejected);
    }

    // --- dry run ---

    [Fact]
    public async Task Dry_run_logs_the_plan_and_writes_nothing()
    {
        var gateway = Gateway(Live with { DryRun = true });

        var outcome = await gateway.ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.DryRun);
        outcome.Planned.Should().HaveCount(26);
        _ec.Writes.Should().BeEmpty();
        outcome.Message.Should().StartWith("Deneme modu");
        _log.Lines.Should().Contain(l => l.Contains("DRY-RUN") && l.Contains("Cool"));
    }

    [Fact]
    public async Task Dry_run_still_validates()
    {
        var outcome = await Gateway(Live with { DryRun = true }).ApplyAsync(Plan(new RegisterWrite(0xF3, 0x83)));

        outcome.Status.Should().Be(WriteStatus.Rejected);
    }

    // --- live writes ---

    [Fact]
    public async Task Applies_and_verifies_a_valid_plan()
    {
        var outcome = await Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.Applied);
        new P65Device(_ec).ReadFanCurves().Cpu.Points.Should().Equal(Presets.Cool.Curves.Cpu.Points);
        new P65Device(_ec).ReadFanCurves().Gpu.Points.Should().Equal(Presets.Cool.Curves.Gpu.Points);
        _log.Lines.Should().Contain(l => l.Contains("Applied") || l.Contains("uygulandı"));
    }

    [Fact]
    public async Task Writes_speeds_before_thresholds_and_fan_mode_last()
    {
        var writes = WritePlans.FanCurves(Presets.Cool.Curves, "Cool").Writes
            .Concat(WritePlans.Fan(FanMode.Advanced).Writes)
            .Reverse()
            .ToArray();

        await Gateway().ApplyAsync(Plan(writes));

        var order = _ec.Writes.Select(w => Category(w.Register)).ToArray();
        order.Should().BeInAscendingOrder();
        _ec.Writes[^1].Register.Should().Be(0xF4);
    }

    private static int Category(byte register) => register switch
    {
        >= 0x72 and <= 0x78 or >= 0x8A and <= 0x90 => 0,
        >= 0x6A and <= 0x6F or >= 0x82 and <= 0x87 => 1,
        0xF4 => 3,
        _ => 2,
    };

    [Fact]
    public async Task Checking_a_fan_plan_never_reads_the_down_offsets()
    {
        var reads = new HashSet<byte>();
        _ec.ReadHook = register =>
        {
            reads.Add(register);
            return null;
        };

        var outcome = await Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.Applied);
        reads.Should().NotContain(r => r >= 0x7A && r <= 0x7F).And.NotContain(r => r >= 0x92 && r <= 0x97);
    }

    [Fact]
    public async Task A_write_that_does_not_stick_the_first_time_is_retried()
    {
        _ec.FlakyOnceRegisters.Add(0xEF);

        var outcome = await Gateway().ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Applied);
        _ec[0xEF].Should().Be(0x80 | 79);
        _ec.Writes.Count(w => w.Register == 0xEF).Should().Be(2);
    }

    // --- failure and recovery ---

    [Fact]
    public async Task Failed_fan_write_restores_factory_table_and_locks_the_gateway()
    {
        _ec.StuckRegisters.Add(0x72);
        _ec[0x72] = 45; // Cool wants 50 here; the EC keeps 45 (the factory value)
        var gateway = Gateway();

        var outcome = await gateway.ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        outcome.Message.Should().StartWith("Ayar uygulanamadı").And.Contain("0x72").And.Contain("geri okunduğunda farklı");
        new P65Device(_ec).ReadFanCurves().Cpu.Points.Should().Equal(FactoryDefaults.FanCurves.Cpu.Points);
        new P65Device(_ec).ReadFanCurves().Gpu.Points.Should().Equal(FactoryDefaults.FanCurves.Gpu.Points);
        gateway.IsWriteEnabled.Should().BeFalse();
        (await gateway.ApplyAsync(WritePlans.ChargeLimit(80))).Status.Should().Be(WriteStatus.Rejected);
    }

    [Fact]
    public async Task When_the_factory_table_cannot_be_restored_cooler_boost_is_tried()
    {
        _ec.StuckRegisters.Add(0x72);
        _ec[0x72] = 99; // neither Cool nor factory value will stick

        var outcome = await Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.FailedUnrecovered);
        (_ec[0x98] & 0x80).Should().Be(0x80);
        (_ec[0x98] & 0x7F).Should().Be(0x02);
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR"));
    }

    [Fact]
    public async Task Without_a_port_recovery_skips_cooler_boost_instead_of_reaching_for_the_port()
    {
        var boostReads = 0;
        _ec.ReadHook = register =>
        {
            boostReads += register == EcMap.CoolerBoost ? 1 : 0;
            return null;
        };
        _ec.StuckRegisters.Add(0x72);
        _ec[0x72] = 99; // neither Cool nor factory value will stick

        var outcome = await Gateway(Live with { PortAvailable = false })
            .ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.FailedUnrecovered);
        boostReads.Should().Be(0);
        _ec.Writes.Should().NotContain(w => w.Register == EcMap.CoolerBoost);
        _log.Lines.Should().Contain(l => l.Contains("port"));
    }

    // --- port plans ---

    [Fact]
    public async Task A_port_plan_whose_watched_registers_cannot_be_read_is_rejected_without_writing()
    {
        var gateway = Gateway();
        // Only the guard reads 0x6A for a charge limit plan.
        _ec.ReadHook = register => register == 0x6A ? throw new EcAccessException("EC silent") : null;

        var outcome = await gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
        gateway.IsWriteEnabled.Should().BeTrue();
        _persistedLocks.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0xEF, 0x80 | 79, 0x72, 50)] // charge limit + fan speed
    [InlineData(0x98, 0x82, 0xF2, 0xC1)] // Cooler Boost + performance mode
    [InlineData(0x98, 0x82, 0xEF, 0x80 | 79)] // both port registers
    public async Task A_port_register_must_be_the_only_write_in_its_plan(byte first, byte firstValue, byte second, byte secondValue)
    {
        var gateway = Gateway();

        var outcome = await gateway.ApplyAsync(Plan(new RegisterWrite(first, firstValue), new RegisterWrite(second, secondValue)));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("tek başına");
        _ec.Writes.Should().BeEmpty();
        gateway.IsWriteEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_non_fan_write_locks_without_touching_the_fan_table()
    {
        _ec.StuckRegisters.Add(0xF2);

        var outcome = await Gateway().ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        _ec.Writes.Should().OnlyContain(w => w.Register == 0xF2);
    }

    [Fact]
    public async Task A_failed_write_becomes_the_lock_reason()
    {
        _ec.StuckRegisters.Add(0xF2);
        var gateway = Gateway();

        await gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        gateway.LockReason.Should().Contain("0xF2");
    }

    [Fact]
    public async Task EC_access_errors_fail_the_write_and_lock_the_gateway()
    {
        var gateway = Gateway();
        _ec.AccessError = new EcAccessException("EC hung");

        var outcome = await gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        outcome.Status.Should().Be(WriteStatus.FailedUnrecovered);
        gateway.IsWriteEnabled.Should().BeFalse();
    }

    // Measured 2026-10-06 under full CPU load: Access_EC was occasionally not free within 500 ms.
    [Fact]
    public async Task A_busy_EC_lock_rejects_the_plan_without_writing_or_locking()
    {
        using var worker = new EcWorker(_ec, new FakeEcLock { Available = false }, TimeSpan.FromMilliseconds(50));
        var gateway = new EcGateway(worker, Live, _log, _persistedLocks.Add, EcAccessRetry.Default with { Sleep = _sleeps.Add });

        var outcome = await gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("Access_EC").And.Contain("alınamadı").And.Contain("hiçbir ayar değişmedi");
        _ec.Writes.Should().BeEmpty();
        gateway.IsWriteEnabled.Should().BeTrue();
        _persistedLocks.Should().BeEmpty();
    }

    // --- silent EC periods (stress measurement 2026-09-30: up to ~250 ms without an answer) ---

    [Fact]
    public async Task A_write_during_a_short_silent_period_is_retried_and_applied()
    {
        var gateway = Gateway();
        _ec.SilentAccesses = 2;

        var outcome = await gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Applied);
        _ec[0xEF].Should().Be(0x80 | 79);
        gateway.IsWriteEnabled.Should().BeTrue();
        _persistedLocks.Should().BeEmpty();
    }

    [Fact]
    public async Task Retries_wait_with_growing_delays()
    {
        _ec.SilentAccesses = 3;

        await Gateway().ApplyAsync(WritePlans.ChargeLimit(79));

        _sleeps.Should().Equal(EcAccessRetry.Default.Delays.Take(3));
        _sleeps.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Reads_before_writing_are_retried_too()
    {
        _ec.SilentAccesses = 1;

        var outcome = await Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.Applied);
    }

    [Fact]
    public async Task Each_retry_is_logged_as_a_warning()
    {
        _ec.SilentAccesses = 1;

        await Gateway().ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        _log.Lines.Where(l => l.StartsWith("WARN ", StringComparison.Ordinal)).Should().ContainSingle().Which.Should().Contain("0xF2").And.Contain("yeniden");
    }

    [Fact]
    public async Task A_silent_period_longer_than_the_retry_window_still_locks()
    {
        var gateway = Gateway();
        _ec.SilentAccesses = EcAccessRetry.Default.Delays.Count + 1;

        var outcome = await gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        outcome.Status.Should().NotBe(WriteStatus.Applied);
        gateway.IsWriteEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_dead_EC_stops_retrying_once_the_plan_sleep_budget_is_spent()
    {
        var retry = EcAccessRetry.Default with { Sleep = _sleeps.Add, SleepBudget = TimeSpan.FromMilliseconds(150) };
        var gateway = new EcGateway(_worker, Live, _log, _persistedLocks.Add, retry);
        _ec.AccessError = new EcAccessException("EC dead");

        var outcome = await gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        outcome.Status.Should().Be(WriteStatus.FailedUnrecovered);
        _sleeps.Should().Equal(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void The_default_sleep_budget_allows_at_least_one_full_retry_window()
    {
        var window = EcAccessRetry.Default.Delays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);

        EcAccessRetry.Default.SleepBudget.Should().BeGreaterThanOrEqualTo(window);
    }

    [Fact]
    public async Task Errors_other_than_EC_access_are_not_retried()
    {
        _ec.AccessError = new InvalidOperationException("bug");

        await Gateway().ApplyAsync(WritePlans.ChargeLimit(79));

        _sleeps.Should().BeEmpty();
    }

    [Fact]
    public void The_default_retry_window_covers_the_longest_measured_silent_period()
    {
        var window = EcAccessRetry.Default.Delays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);

        window.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(2 * 252));
    }

    // --- review findings (2026-09-30) ---

    [Fact]
    public async Task A_plan_queued_before_a_failure_is_not_written_after_the_lock()
    {
        _ec.StuckRegisters.Add(0xF2);
        var gateway = Gateway();

        var first = gateway.ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));
        var second = gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        (await first).Status.Should().Be(WriteStatus.FailedRecovered);
        (await second).Status.Should().Be(WriteStatus.Rejected);
        _ec.Writes.Should().NotContain(w => w.Register == 0xEF);
    }

    [Fact]
    public async Task A_throwing_logger_cannot_skip_recovery()
    {
        _ec.StuckRegisters.Add(0x72);
        _ec[0x72] = 45;

        var outcome = await Gateway(log: new ThrowingLog()).ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.FailedRecovered);
        outcome.Message.Should().StartWith("Ayar uygulanamadı").And.Contain("0x72").And.Contain("geri okunduğunda farklı");
        new P65Device(_ec).ReadFanCurves().Cpu.Points.Should().Equal(FactoryDefaults.FanCurves.Cpu.Points);
    }

    [Fact]
    public async Task Switching_to_advanced_mode_is_rejected_when_the_current_table_is_unsafe()
    {
        _ec[0x78] = 50; // last CPU step at 50 %: below the safety floor

        var outcome = await Gateway().ApplyAsync(WritePlans.Fan(FanMode.Advanced));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task The_plan_is_snapshotted_so_later_changes_to_the_list_are_ignored()
    {
        using var gate = new ManualResetEventSlim();
        var blocker = _worker.RunAsync(_ => gate.Wait(TimeSpan.FromSeconds(5)));
        var writes = new List<RegisterWrite> { new(0xEF, 0x80 | 79) };

        var pending = Gateway().ApplyAsync(new WritePlan("mutable", writes));
        writes.Add(new RegisterWrite(0xF3, 0x83));
        gate.Set();
        await blocker;

        (await pending).Status.Should().Be(WriteStatus.Applied);
        _ec.Writes.Should().NotContain(w => w.Register == 0xF3);
    }

    [Fact]
    public async Task Cooler_boost_recovery_is_skipped_when_the_register_cannot_be_read_consistently()
    {
        _ec.StuckRegisters.Add(0x72);
        _ec[0x72] = 99;
        byte flip = 0;
        _ec.ReadHook = register => register == 0x98 ? (byte)(flip++ % 2 == 0 ? 0x02 : 0x42) : null;

        var outcome = await Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"));

        outcome.Status.Should().Be(WriteStatus.FailedUnrecovered);
        _ec.Writes.Should().NotContain(w => w.Register == 0x98);
    }

    [Fact]
    public async Task A_failure_is_persisted_so_the_next_session_starts_locked()
    {
        _ec.StuckRegisters.Add(0xF2);

        await Gateway().ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced));

        _persistedLocks.Should().ContainSingle().Which.Should().Contain("0xF2");
    }

    [Fact]
    public async Task A_persisted_lock_keeps_a_new_gateway_locked()
    {
        var gateway = Gateway(Live with { PersistedLockReason = "önceki oturumda yazma başarısız" });

        var outcome = await gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        outcome.Message.Should().Contain("önceki oturumda");
        _ec.Writes.Should().BeEmpty();
        gateway.LockReason.Should().Be("önceki oturumda yazma başarısız");
    }

    private sealed class ThrowingLog : IAppLog
    {
        public void Info(string message) => throw new UnauthorizedAccessException("log");

        public void Warn(string message) => throw new UnauthorizedAccessException("log");

        public void Error(string message, Exception? exception = null) => throw new UnauthorizedAccessException("log");
    }
}
