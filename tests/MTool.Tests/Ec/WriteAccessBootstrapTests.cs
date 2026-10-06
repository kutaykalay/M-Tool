using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

public sealed class WriteAccessBootstrapTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 22, 0, 0, TimeSpan.FromHours(3));

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeEcRegisters _ec = P65Memory.FactorySnapshot();
    private readonly ListLog _log = new();
    private readonly List<TimeSpan> _sleeps = [];
    private readonly EcWorker _worker;

    public WriteAccessBootstrapTests() =>
        _worker = new EcWorker(_ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50));

    public void Dispose()
    {
        _worker.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private string PreStatePath => Path.Combine(_folder, "pre-mtool-state.json");

    private Task<WriteAccessSetup> CreateAsync(bool dryRun = false, bool portAvailable = true) => WriteAccessBootstrap.CreateAsync(
        _worker, TestLayouts.P65, _folder, dryRun, portAvailable, _log, EcAccessRetry.Default with { Sleep = _sleeps.Add }, () => Now);

    [Fact]
    public async Task Without_a_port_the_gateway_opens_but_refuses_port_plans()
    {
        var setup = await CreateAsync(portAvailable: false);

        var outcome = await setup.Gateway.ApplyAsync(WritePlans.ChargeLimit(79));

        outcome.Status.Should().Be(WriteStatus.Rejected);
        setup.Gateway.IsWriteEnabled.Should().BeTrue();
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Supported_firmware_without_a_backup_takes_one_and_opens_writes()
    {
        var setup = await CreateAsync();

        setup.Firmware!.Version.Should().Be("16Q4EMS2.107");
        setup.Gateway.IsWriteEnabled.Should().BeTrue();
        new PreStateStore(_folder).Load()!.CapturedAt.Should().Be(Now);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task An_existing_valid_backup_is_kept()
    {
        await CreateAsync();
        var before = File.ReadAllText(PreStatePath);
        _ec[0xEF] = 0xD5;

        var setup = await CreateAsync();

        setup.Gateway.IsWriteEnabled.Should().BeTrue();
        File.ReadAllText(PreStatePath).Should().Be(before);
    }

    [Fact]
    public async Task Unsupported_firmware_stays_read_only_and_takes_no_backup()
    {
        _ec.LoadAscii(0xA0, "16Q4EMS2.108");

        var setup = await CreateAsync();

        setup.Firmware!.Version.Should().Be("16Q4EMS2.108");
        setup.Gateway.LockReason.Should().Contain("firmware");
        File.Exists(PreStatePath).Should().BeFalse();
    }

    [Fact]
    public async Task A_silent_first_access_is_retried()
    {
        _ec.SilentAccesses = 1;

        var setup = await CreateAsync();

        setup.Firmware!.IsSupported.Should().BeTrue();
        setup.Gateway.IsWriteEnabled.Should().BeTrue();
        _sleeps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_firmware_locks_writes_instead_of_throwing()
    {
        _ec.AccessError = new EcAccessException("EC hung");

        var setup = await CreateAsync();

        setup.Firmware.Should().BeNull();
        setup.Gateway.IsWriteEnabled.Should().BeFalse();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("Firmware"));
    }

    [Fact]
    public async Task A_backup_whose_two_passes_disagree_is_not_saved_and_locks_writes()
    {
        var reads = 0;
        _ec.ReadHook = register => register == 0xEF ? (byte)(++reads % 2 == 0 ? 0xD0 : 0xD1) : null;

        var setup = await CreateAsync();

        setup.Gateway.LockReason.Should().Contain("yedeği");
        File.Exists(PreStatePath).Should().BeFalse();
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR") && l.Contains("0xEF"));
    }

    [Fact]
    public async Task A_corrupt_backup_is_left_alone_and_locks_writes()
    {
        File.WriteAllText(PreStatePath, "{ bozuk");

        var setup = await CreateAsync();

        setup.Gateway.LockReason.Should().Contain("yedeği");
        File.ReadAllText(PreStatePath).Should().Be("{ bozuk");
    }

    [Fact]
    public async Task A_persisted_write_lock_keeps_writes_locked()
    {
        new WriteLockStore(_folder).Lock("önceki yazma başarısız");

        var setup = await CreateAsync();

        setup.Gateway.LockReason.Should().Contain("önceki yazma başarısız");
    }

    [Fact]
    public async Task Dry_run_is_passed_to_the_gateway()
    {
        var setup = await CreateAsync(dryRun: true);

        setup.Gateway.IsDryRun.Should().BeTrue();
    }

    [Fact]
    public async Task A_backup_with_null_entries_locks_writes_instead_of_throwing()
    {
        File.WriteAllText(PreStatePath, """{ "firmwareVersion": "16Q4EMS2.107", "firmwareDate": "05132019", "registers": [null] }""");

        var setup = await CreateAsync();

        setup.Gateway.LockReason.Should().Contain("yedeği");
    }

    [Fact]
    public async Task An_unreadable_backup_file_locks_writes_instead_of_throwing()
    {
        await CreateAsync();
        using var held = new FileStream(PreStatePath, FileMode.Open, FileAccess.Read, FileShare.None);

        var setup = await CreateAsync();

        setup.Gateway.LockReason.Should().Contain("yedeği");
        _log.Lines.Should().Contain(l => l.StartsWith("ERROR"));
    }
}
