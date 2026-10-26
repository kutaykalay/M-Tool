using System.Text;
using MTool.App.Hardware;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Settings;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Hardware;

/// <summary>
/// The writable WMI2 source (plan asama15 15c-4): built only for the firmware the user consented to and only on an
/// exact list entry; its gateway shares the live source's EC worker, so reads and writes queue on one thread.
/// </summary>
public sealed class Wmi2ControlSourceTests : IDisposable
{
    private const string Firmware = "15M3EMS1.105";

    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.Load(
        """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105"],
          "families": ["15M3EMS1.1"],
          "familyOverrides": {}
        }
        """,
        [P65()]);

    private readonly SimulatedWmi2Ec _ec = SimulatedWmi2Ec.Typical();
    private readonly Owned _owned = new();
    private readonly ListLog _log = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-wmi2-control-").FullName;
    private string _firmware = Firmware;
    private bool _wmi2Only = true;
    private Wmi2ControlSource? _source;

    public Wmi2ControlSourceTests() =>
        _ec.ReadAnswer = r => r.Method == "Get_EC" ? EcPacket(_firmware) : null;

    public void Dispose()
    {
        _source?.Dispose();
        _ec.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private Wmi2LiveSource Live() =>
        new(_ec, new Wmi2WritableRegisters(_ec, _ec), _owned, new FakeEcLock(), () => true, _log);

    private Wmi2ControlOptions Options(string consented = Firmware) =>
        new(consented, List, PreStateStore.ForWmi2(_folder), () => _wmi2Only, DryRun: false);

    private async Task<Wmi2ControlOpening> Opening(Wmi2ControlOptions? options = null, CancellationToken cancellationToken = default)
    {
        var opening = await Wmi2ControlSource.CreateAsync(Live(), options ?? Options(), _log, cancellationToken);
        _source = opening.Source;
        return opening;
    }

    private async Task<Wmi2ControlSource?> Create(Wmi2ControlOptions? options = null) => (await Opening(options)).Source;

    private async Task NotOpened(Wmi2ControlUnavailable reason, Wmi2ControlOptions? options = null)
    {
        var opening = await Opening(options);

        opening.Source.Should().BeNull();
        opening.Unavailable.Should().Be(reason);
        _owned.Disposed.Should().BeTrue("nothing is left open");
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task The_consented_exact_firmware_gets_a_wmi2_gateway_on_the_same_worker()
    {
        var source = await Create();

        source.Should().NotBeNull();
        source!.Gateway.Kind.Should().Be(EcGatewayKind.Wmi2Experimental);
        (await source.Gateway.TestWmi2WritePathAsync()).Status.Should().Be(WriteStatus.Applied);
        (await source.ReadStatusAsync(CancellationToken.None)).PerformanceRaw.Should().Be(0xC1);
        _ec.Writes.Should().Equal(Wmi2Writes.Request(0xD2, 0xC1));
    }

    [Theory]
    [InlineData("15M3EMS1.104")] // consent for the firmware before an update
    [InlineData("")]
    public async Task Consent_for_another_firmware_builds_nothing_and_closes_the_live_source(string consented)
    {
        await NotOpened(Wmi2ControlUnavailable.OtherFirmware, Options(consented));
    }

    [Fact]
    public async Task A_family_match_only_reads()
    {
        _firmware = "15M3EMS1.199";

        await NotOpened(Wmi2ControlUnavailable.NotExactFirmware, Options("15M3EMS1.199"));
    }

    [Fact]
    public async Task An_unreadable_firmware_builds_nothing()
    {
        _ec.ReadAnswer = r => r.Method == "Get_EC" ? FakeWmi2Methods.Packet(0, status: 0) : null;

        await NotOpened(Wmi2ControlUnavailable.FirmwareUnreadable);
    }

    [Fact]
    public async Task A_layout_mismatch_closes_every_write_and_names_the_raw_bytes()
    {
        _ec[0xD2] = 0x00;

        var source = await Create();
        var outcome = await source!.Gateway.TestWmi2WritePathAsync();

        outcome.Block.Should().Be(Wmi2WriteBlock.LayoutNotVerified);
        _ec.Writes.Should().BeEmpty();
        _log.Lines.Should().Contain(l => l.Contains("0xD2=0x00"));
    }

    [Fact]
    public async Task A_wmi1_answer_right_before_the_write_refuses_it()
    {
        var source = await Create();
        _wmi2Only = false;

        var outcome = await source!.Gateway.TestWmi2WritePathAsync();

        outcome.Block.Should().Be(Wmi2WriteBlock.NotWmi2Only);
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_status_read_builds_nothing()
    {
        _ec.ReadAnswer = r => r.Method == "Get_EC" ? EcPacket(Firmware) : r.Sub == 0xD2 ? FakeWmi2Methods.Packet(0, status: 0) : null;

        var create = () => Create();

        await create.Should().ThrowAsync<EcAccessException>();
        _owned.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task A_cancelled_opening_closes_the_live_source()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var open = () => Opening(cancellationToken: cancelled.Token);

        await open.Should().ThrowAsync<OperationCanceledException>();
        _owned.Disposed.Should().BeTrue();
    }

    [Fact]
    public void An_opening_is_control_or_a_reason()
    {
        var opened = () => Wmi2ControlOpening.Opened(null!);

        opened.Should().Throw<ArgumentNullException>();
        Wmi2ControlOpening.NotOpened(Wmi2ControlUnavailable.NotWmi2).Source.Should().BeNull();
    }

    [Fact]
    public async Task Disposing_closes_the_live_source()
    {
        var source = await Create();

        source!.Dispose();

        _owned.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(WmiInterface.Wmi2, true)]
    [InlineData(WmiInterface.Wmi1, false)]
    [InlineData(null, false)]
    public void Only_a_wmi2_answer_counts_as_wmi2_only(WmiInterface? detected, bool expected)
    {
        Wmi2ControlSource.IsWmi2Only(() => detected).Should().Be(expected);
    }

    [Fact]
    public async Task The_read_only_live_source_cannot_write()
    {
        using var live = new Wmi2LiveSource(_ec, new Owned(), new FakeEcLock(), () => true, _log);

        var write = () => live.Worker.RunWriteAsync(registers => { registers.Write(0xD2, 0xC4); return 0; });

        await write.Should().ThrowAsync<InvalidOperationException>();
        _ec.Writes.Should().BeEmpty();
    }

    [Fact]
    public void Only_the_control_path_opens_a_writable_session_or_writable_registers()
    {
        string[] allowed = ["MsiWmi2Session.cs", "Wmi2LiveSource.cs"];
        var users = RepoFiles.Under("src", "*.cs")
            .Where(file => File.ReadAllText(file) is var code && (code.Contains("OpenWritable") || code.Contains("new Wmi2WritableRegisters(")))
            .Select(Path.GetFileName);

        users.Should().BeSubsetOf(allowed, "a writable WMI2 session exists only behind Wmi2ControlSource");
        File.ReadAllText(Path.Combine(RepoFiles.Root, "src", "MTool.App", "Hardware", "Wmi2LiveSource.cs"))
            .Split("OpenWritable").Should().HaveCount(2, "one call, inside OpenForControl");
        RepoFiles.Under("src", "*.cs").Where(file => File.ReadAllText(file).Contains("OpenForControl(")).Select(Path.GetFileName)
            .Should().BeEquivalentTo(["Wmi2LiveSource.cs", "Wmi2ControlSource.cs"]);
    }

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
