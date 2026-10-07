using System.Text.Json;
using System.Text.Json.Nodes;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device.Config;

/// <summary>A record may cover several firmware families (7f: the A′ record lists three models).</summary>
public class FirmwareFamiliesTests
{
    private static DeviceConfig Legacy(params string[] families) =>
        Draft() with { Id = "legacy", Firmware = new FirmwareSpec([], families) };

    [Fact]
    public void Any_of_a_records_families_matches()
    {
        FirmwareMatcher.Match(new FirmwareInfo("1782EMS1.109", ""), [P65(), Legacy("16P5EMS1.1", "1782EMS1.1")])
            .Should().Be(new DeviceMatch(MatchKind.Family, "legacy", "MSI P65 Creator 9SE"));
    }

    [Fact]
    public void A_record_without_families_matches_only_exactly()
    {
        FirmwareMatcher.Match(new FirmwareInfo("16P5EMS1.103", ""), [Legacy()]).Should().Be(DeviceMatch.None);
    }

    [Fact]
    public void A_family_listed_twice_is_rejected()
    {
        DeviceConfigValidator.Validate(Legacy("16P5EMS1.1", "16P5EMS1.1")).Should().ContainMatch("*16P5EMS1.1*iki kez*");
    }

    [Fact]
    public void Every_family_must_have_ten_characters()
    {
        DeviceConfigValidator.Validate(Legacy("16P5EMS1.1", "1782EMS1")).Should().ContainMatch("*1782EMS1*10 karakter*");
    }

    [Fact]
    public void An_exact_firmware_must_belong_to_one_of_the_families()
    {
        var config = P65() with { Firmware = new FirmwareSpec(["16Q4EMS2.107"], ["16P5EMS1.1", "1782EMS1.1"]) };

        DeviceConfigValidator.Validate(config).Should().ContainMatch("*16Q4EMS2.107*ailelerinde değil*");
    }

    [Fact]
    public void Two_records_may_not_share_any_family()
    {
        var other = Legacy("1799EMS1.1", "16Q4EMS2.1") with { Id = "other" };

        DeviceConfigValidator.FindConflicts([P65(), other]).Select(c => c.Id)
            .Should().BeEquivalentTo("msi-p65-creator-9se", "other");
    }

    [Fact]
    public void Json_lists_the_families()
    {
        var json = JsonSerializer.Serialize(Legacy("16P5EMS1.1", "1782EMS1.1"), DeviceConfigLoader.Options);

        JsonNode.Parse(json)!["firmware"]!["families"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().Equal("16P5EMS1.1", "1782EMS1.1");
        DeviceConfigLoader.Parse(json).Firmware.Families.Should().Equal("16P5EMS1.1", "1782EMS1.1");
    }

    [Fact]
    public void The_old_single_family_key_is_rejected()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(P65(), DeviceConfigLoader.Options))!;
        node["firmware"]!.AsObject().Remove("families");
        node["firmware"]!["family"] = "16Q4EMS2.1";

        var act = () => DeviceConfigLoader.Parse(node.ToJsonString());

        act.Should().Throw<JsonException>();
    }
}
