using System.Text;
using MTool.App.Hardware;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Hardware;

public sealed class Wmi2LiveSourceTests : IDisposable
{
    private readonly FakeWmi2Methods _wmi = new();
    private readonly FakeEcLock _lock = new();
    private readonly ListLog _log = new();
    private readonly Owned _owned = new();
    private bool _gateOpen = true;
    private Wmi2LiveSource? _source;

    private Wmi2LiveSource Source => _source ??= new Wmi2LiveSource(_wmi, _owned, _lock, () => _gateOpen, _log);

    public void Dispose() => _source?.Dispose();

    [Fact]
    public async Task Sensors_are_read_only_from_the_live_list_each_register_once_under_the_lock()
    {
        _wmi[0x68] = 61;
        _wmi[0x71] = 45;
        _wmi[0xC9] = 0xA0;

        var snapshot = await Source.ReadSensorsAsync(CancellationToken.None);

        _wmi.Calls.Should().BeEquivalentTo(Wmi2Calls.Live);
        _wmi.Calls.Should().OnlyHaveUniqueItems();
        snapshot.CpuTempC.Should().Be(61);
        snapshot.CpuFanPercent.Should().Be(45);
        snapshot.CpuRpm.Should().Be(2987);
        _lock.Events.Select(e => e.Action).Should().Equal("acquire", "release");
    }

    [Fact]
    public async Task Status_is_read_only_from_the_status_list()
    {
        _wmi[0xD2] = 0xC4;

        var status = await Source.ReadStatusAsync(CancellationToken.None);

        _wmi.Calls.Should().BeEquivalentTo(Wmi2Calls.Status);
        status.PerformanceRaw.Should().Be(0xC4);
    }

    [Fact]
    public async Task Firmware_is_one_get_ec_call()
    {
        _wmi.AnswerHook = request => request.Method == "Get_EC" ? EcPacket("16S1EMS1.104") : null;

        var firmware = await Source.ReadFirmwareAsync(CancellationToken.None);

        _wmi.Calls.Should().Equal(Wmi2Calls.Request("Get_EC", 0));
        firmware.Should().Be("16S1EMS1.104");
    }

    [Fact]
    public async Task A_failed_get_ec_answer_has_no_firmware()
    {
        _wmi.AnswerHook = _ => FakeWmi2Methods.Packet(0, status: 0);

        var firmware = await Source.ReadFirmwareAsync(CancellationToken.None);

        firmware.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_get_ec_call_throws()
    {
        _wmi.Error = new EcAccessException("MSI_ACPI Get_EC(0x00) did not answer within 2 s");

        await FluentActions.Awaiting(() => Source.ReadFirmwareAsync(CancellationToken.None))
            .Should().ThrowAsync<EcAccessException>();
    }

    [Fact]
    public async Task While_the_gate_is_closed_nothing_is_called()
    {
        _gateOpen = false;

        var read = () => Source.ReadSensorsAsync(CancellationToken.None);

        (await read.Should().ThrowAsync<EcAccessException>()).Which.IsAccessPaused.Should().BeTrue();
        _wmi.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_closes_the_interface_after_the_worker_and_reads_nothing_more()
    {
        await Source.ReadStatusAsync(CancellationToken.None);

        Source.Dispose();

        _owned.Disposed.Should().BeTrue();
        await FluentActions.Awaiting(() => Source.ReadStatusAsync(CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
        _wmi.Calls.Should().HaveCount(Wmi2Calls.Status.Count);
    }

    /// <summary>A Get_EC answer: success byte, then the firmware version from byte 2 (Wmi2Readout).</summary>
    private static byte[] EcPacket(string firmware)
    {
        var packet = FakeWmi2Methods.Packet(0);
        Encoding.ASCII.GetBytes(firmware).CopyTo(packet, 2);
        return packet;
    }

    private sealed class Owned : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
