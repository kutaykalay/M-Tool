using MTool.Core.Device;
using MTool.Core.Device.Config;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device.Config;

public class FirmwareMatcherTests
{
    private static readonly IReadOnlyList<DeviceConfig> Catalog = [P65()];

    [Fact]
    public void The_verified_firmware_is_an_exact_match()
    {
        FirmwareMatcher.Match(new FirmwareInfo("16Q4EMS2.107", ""), Catalog)
            .Should().Be(new DeviceMatch(MatchKind.Exact, "msi-p65-creator-9se", "MSI P65 Creator 9SE"));
    }

    [Fact]
    public void Another_firmware_of_the_same_family_is_a_family_match()
    {
        FirmwareMatcher.Match(new FirmwareInfo("16Q4EMS2.108", ""), Catalog)
            .Should().Be(new DeviceMatch(MatchKind.Family, "msi-p65-creator-9se", "MSI P65 Creator 9SE"));
    }

    [Theory]
    [InlineData("16Q4EMS1.110")] // another board: n differs
    [InlineData("16Q4EMS2.207")] // another board generation: y differs
    [InlineData("1541EMS1.115")]
    [InlineData("16Q4EMS2")]
    [InlineData("")]
    public void Anything_else_matches_nothing(string firmware)
    {
        FirmwareMatcher.Match(new FirmwareInfo(firmware, ""), Catalog).Should().Be(DeviceMatch.None);
    }

    [Fact]
    public void An_unreadable_firmware_matches_nothing()
    {
        FirmwareMatcher.Match(null, Catalog).Should().Be(DeviceMatch.None);
    }

    [Fact]
    public void An_exact_match_wins_over_another_records_family()
    {
        var family = Draft() with { Id = "family-only", Firmware = new FirmwareSpec([], "16Q4EMS2.1") };

        FirmwareMatcher.Match(new FirmwareInfo("16Q4EMS2.107", ""), [family, P65()]).RecordId.Should().Be("msi-p65-creator-9se");
    }

    [Theory]
    [InlineData("16Q4EMS2.107")]
    [InlineData("16Q4EMS2.108")]
    [InlineData("16Q4EMS1.110")]
    [InlineData("")]
    public void A_family_or_no_match_is_never_supported_firmware(string firmware)
    {
        var info = new FirmwareInfo(firmware, "");

        var match = FirmwareMatcher.Match(info, Catalog);

        if (match.Kind != MatchKind.Exact)
        {
            info.IsSupported.Should().BeFalse();
        }
    }

    [Fact]
    public void A_later_record_can_match()
    {
        var other = Draft() with { Id = "other", Firmware = new FirmwareSpec([], "1541EMS1.1") };

        FirmwareMatcher.Match(new FirmwareInfo("1541EMS1.115", ""), [P65(), other]).RecordId.Should().Be("other");
    }

    [Fact]
    public void A_firmware_as_long_as_the_family_matches_the_family()
    {
        FirmwareMatcher.Match(new FirmwareInfo("16Q4EMS2.1", ""), Catalog).Kind.Should().Be(MatchKind.Family);
    }

    [Fact]
    public void The_log_text_names_the_kind_and_the_record()
    {
        new DeviceMatch(MatchKind.Family, "msi-p65-creator-9se", "MSI P65 Creator 9SE").ToString()
            .Should().Be("Family (msi-p65-creator-9se)");
        DeviceMatch.None.ToString().Should().Be("eşleşme yok");
    }
}
