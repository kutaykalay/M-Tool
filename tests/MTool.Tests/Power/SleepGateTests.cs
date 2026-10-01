using Microsoft.Extensions.Time.Testing;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Power;

/// <summary>The sleep gate end to end: coordinator → worker → gateway, against a fake EC.</summary>
public sealed class SleepGateTests : IDisposable
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);
    private static readonly FirmwareInfo Firmware = new("16Q4EMS2.107", "05132019");

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeTimeProvider _time = new();
    private readonly FakeEcRegisters _ec = P65Memory.FactorySnapshot();
    private readonly ListLog _log = new();
    private readonly PowerStateCoordinator _coordinator;
    private readonly EcWorker _worker;
    private readonly P65Control _control;

    public SleepGateTests()
    {
        _coordinator = new PowerStateCoordinator(_time, AutoReapplyOptions.Default.GateDelay);
        _worker = new EcWorker(_ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50), accessGate: () => _coordinator.IsEcAccessAllowed);
        var retry = EcAccessRetry.Default with { Sleep = _ => { } };
        _control = new P65Control(_worker, new WriteAccessSetup(Firmware, new EcGateway(_worker, Live, _log, retry: retry)), _log, retry);
    }

    public void Dispose()
    {
        _worker.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task A_write_while_asleep_is_rejected_without_touching_the_ec()
    {
        _coordinator.OnSuspend();

        var outcome = await _control.SetPerformanceAsync(PerformanceMode.Balanced);

        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Accesses.Should().Be(0);
        _control.Access.WriteMode.Should().Be(WriteMode.Enabled);
    }

    [Fact]
    public async Task A_reapply_while_asleep_writes_nothing()
    {
        var desired = DesiredState.Default with { FanProfile = "Cool", ChargeLimitPercent = 80 };
        var service = new ProfileService(
            _control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default with { Desired = desired }, _log);
        _coordinator.OnSuspend();

        var outcomes = await service.ReapplyAsync(PortUse.None);

        ReapplySummary.Worst(outcomes)!.Status.Should().Be(WriteStatus.Rejected);
        _ec.Accesses.Should().Be(0);
    }

    [Fact]
    public async Task Writes_work_again_once_the_gate_delay_has_passed()
    {
        _coordinator.OnSuspend();
        _coordinator.OnResume();
        _time.Advance(AutoReapplyOptions.Default.GateDelay);

        var outcome = await _control.SetPerformanceAsync(PerformanceMode.Balanced);

        outcome.Status.Should().Be(WriteStatus.Applied);
    }
}
