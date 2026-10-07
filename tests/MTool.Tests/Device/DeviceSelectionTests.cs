using MTool.Core.Device;
using MTool.Core.Device.Config;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device;

/// <summary>Which record a session reads with, and whether it may open the raw port (7f).</summary>
public class DeviceSelectionTests
{
    private static readonly IReadOnlyList<DeviceConfig> Catalog = [P65()];

    [Fact]
    public void The_verified_firmware_gets_its_record_and_the_port()
    {
        var selection = DeviceSelection.Choose(new FirmwareInfo("16Q4EMS2.107", "05132019"), Catalog, P65().Id);

        selection.Record.Id.Should().Be("msi-p65-creator-9se");
        selection.Match.Kind.Should().Be(MatchKind.Exact);
        selection.PortAllowed.Should().BeTrue();
    }

    [Fact]
    public void Another_bios_of_the_same_family_reads_with_the_record_but_without_the_port()
    {
        var selection = DeviceSelection.Choose(new FirmwareInfo("16Q4EMS2.108", ""), Catalog, P65().Id);

        selection.Record.Id.Should().Be("msi-p65-creator-9se");
        selection.Match.Kind.Should().Be(MatchKind.Family);
        selection.PortAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("1541EMS1.115")]
    [InlineData("")]
    public void An_unknown_firmware_falls_back_without_the_port(string firmware)
    {
        var selection = DeviceSelection.Choose(new FirmwareInfo(firmware, ""), Catalog, P65().Id);

        selection.Record.Id.Should().Be("msi-p65-creator-9se");
        selection.Match.Should().Be(DeviceMatch.None);
        selection.PortAllowed.Should().BeFalse();
    }

    [Fact]
    public void An_unreadable_firmware_falls_back_without_the_port()
    {
        DeviceSelection.Choose(null, Catalog, P65().Id).PortAllowed.Should().BeFalse();
    }

    // Each of the three conditions on its own: the other two hold, only this one fails.

    [Fact]
    public void The_verified_firmware_reached_only_by_family_gets_no_port()
    {
        var p65WithoutIt = P65() with { Firmware = new FirmwareSpec(["16Q4EMS2.106"], ["16Q4EMS2.1"]) };

        var selection = DeviceSelection.Choose(new FirmwareInfo("16Q4EMS2.107", ""), [p65WithoutIt], P65().Id);

        selection.Match.Kind.Should().Be(MatchKind.Family);
        selection.PortAllowed.Should().BeFalse();
    }

    [Fact]
    public void The_verified_firmware_on_a_record_not_verified_for_writes_gets_no_port()
    {
        var readOnly = P65() with { Status = DeviceStatus.ReadVerified };

        var selection = DeviceSelection.Choose(new FirmwareInfo("16Q4EMS2.107", ""), [readOnly], P65().Id);

        selection.Match.Kind.Should().Be(MatchKind.Exact);
        selection.PortAllowed.Should().BeFalse();
    }

    [Fact]
    public void An_exact_match_on_a_firmware_outside_the_code_allowlist_gets_no_port()
    {
        var other = P65() with { Id = "other", Firmware = new FirmwareSpec(["1541EMS1.115"], []) };

        var selection = DeviceSelection.Choose(new FirmwareInfo("1541EMS1.115", ""), [P65(), other], P65().Id);

        selection.Record.Id.Should().Be("other");
        selection.Match.Kind.Should().Be(MatchKind.Exact);
        selection.PortAllowed.Should().BeFalse();
    }

    [Fact]
    public void A_missing_fallback_record_is_an_error()
    {
        var act = () => DeviceSelection.Choose(null, Catalog, "nope");

        act.Should().Throw<InvalidOperationException>().WithMessage("*nope*");
    }
}
