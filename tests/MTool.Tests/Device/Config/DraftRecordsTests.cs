using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device.Config;

/// <summary>The two read-only family records of 7f: generic WMI1 (A) and legacy WMI1 (A′).</summary>
public sealed class DraftRecordsTests : IDisposable
{
    private static readonly IReadOnlyList<DeviceConfig> Catalog = DeviceConfigLoader.LoadEmbedded(new ListLog());
    private static readonly DeviceConfig P65 = Catalog.Single(c => c.Id == EmbeddedDevices.P65Id);
    private static readonly DeviceConfig Generic = Catalog.Single(c => c.Id == EmbeddedDevices.Wmi1GenericId);
    private static readonly DeviceConfig Legacy = Catalog.Single(c => c.Id == "msi-wmi1-legacy");

    private readonly FakeEcRegisters _ec = P65Memory.FactorySnapshot();
    private readonly EcWorker _worker;

    public DraftRecordsTests() => _worker = new EcWorker(_ec, new FakeEcLock(), TimeSpan.FromMilliseconds(50));

    public void Dispose() => _worker.Dispose();

    [Fact]
    public void Three_records_load_without_conflicts()
    {
        var log = new ListLog();

        DeviceConfigLoader.LoadEmbedded(log).Select(c => c.Id)
            .Should().BeEquivalentTo("msi-p65-creator-9se", "msi-wmi1-generic", "msi-wmi1-legacy");
        log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void No_two_embedded_records_share_a_firmware_or_a_family()
    {
        // The matcher takes the first family match, so the catalog must never overlap.
        DeviceConfigValidator.FindConflicts(Catalog).Should().BeEmpty();
    }

    [Fact]
    public void Both_new_records_are_drafts_with_a_layout()
    {
        foreach (var record in new[] { Generic, Legacy })
        {
            record.Status.Should().Be(DeviceStatus.Draft);
            record.Firmware.Exact.Should().BeEmpty();
            record.Presets.Should().BeEmpty();
            DeviceLayout.From(record).Should().NotBeNull();
        }
    }

    [Fact]
    public void The_generic_record_reads_the_p65_registers_and_matches_no_firmware()
    {
        Generic.Firmware.Families.Should().BeEmpty();
        var generic = DeviceLayout.From(Generic);
        var p65 = DeviceLayout.From(P65);

        generic.Wmi.Fields.Should().BeEquivalentTo(p65.Wmi.Fields);
        generic.WatchedRegisters.Should().Equal(p65.WatchedRegisters);
        generic.Capabilities.Should().BeEquivalentTo(p65.Capabilities, o => o.WithStrictOrdering());
    }

    [Fact]
    public void The_legacy_record_has_no_charge_limit_and_its_own_fan_mode_values()
    {
        Legacy.Firmware.Families.Should().Equal("16P5EMS1.1", "1782EMS1.1", "1799EMS1.1");
        Legacy.Features.ChargeLimit.Should().BeNull();
        Legacy.Features.FanMode.Should().Be(new FanModeFeature(0xF4, Auto: 0x0C, Advanced: 0x8C));
        Legacy.PortRegisters.Should().Equal(0x98);
        DeviceLayout.From(Legacy).Capabilities.ChargeLimit.Should().BeFalse();
    }

    [Theory]
    [InlineData("16Q4EMS2.107", "msi-p65-creator-9se", MatchKind.Exact, true)]
    [InlineData("16Q4EMS2.108", "msi-p65-creator-9se", MatchKind.Family, false)]
    [InlineData("16P5EMS1.103", "msi-wmi1-legacy", MatchKind.Family, false)]
    [InlineData("1799EMS1.112", "msi-wmi1-legacy", MatchKind.Family, false)]
    [InlineData("1541EMS1.115", "msi-wmi1-generic", MatchKind.None, false)]
    public void Each_firmware_gets_its_record(string firmware, string record, MatchKind kind, bool port)
    {
        var selection = DeviceSelection.Choose(new FirmwareInfo(firmware, ""), Catalog, EmbeddedDevices.Wmi1GenericId);

        selection.Record.Id.Should().Be(record);
        selection.Match.Kind.Should().Be(kind);
        selection.PortAllowed.Should().Be(port);
    }

    [Theory]
    [InlineData("msi-wmi1-generic", "1541EMS1.115")]
    [InlineData("msi-wmi1-legacy", "16P5EMS1.103")]
    public async Task A_draft_record_never_writes(string id, string firmware)
    {
        var layout = DeviceLayout.From(Catalog.Single(c => c.Id == id)).WithoutPortFeatures();
        var policy = new WritePolicy(FirmwareSupported: new FirmwareInfo(firmware, "").IsSupported, PreStateSaved: true, DryRun: false, PortAvailable: false);
        var control = new P65Control(_worker, new WriteAccessSetup(new FirmwareInfo(firmware, ""), new EcGateway(_worker, policy, new ListLog())), layout, new ListLog());

        var outcome = await control.SetPerformanceAsync(PerformanceMode.High);

        control.Access.WriteMode.Should().Be(WriteMode.Locked);
        outcome.Status.Should().Be(WriteStatus.Rejected);
        _ec.Writes.Should().BeEmpty();
    }
}
