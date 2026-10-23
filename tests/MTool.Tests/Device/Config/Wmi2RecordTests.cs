using System.Security.Cryptography;
using System.Text;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device.Config;

/// <summary>The embedded msi-ec WMI2 firmware list (plan asama15 15b-2), as tools/msiec-import wrote it.</summary>
public class Wmi2RecordTests
{
    private static readonly IReadOnlyList<DeviceConfig> Catalog = DeviceConfigLoader.LoadEmbedded(new ListLog());

    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.LoadEmbedded(Catalog);

    [Fact]
    public void The_embedded_list_loads_and_passes_the_apps_rules()
    {
        Wmi2FirmwareList.Validate(List, Catalog).Should().BeEmpty();
    }

    [Fact]
    public void The_counts_are_the_ones_the_script_reported()
    {
        // msi-ec d7fbbd8, CONF_G2_0…G2_6 and G2_10; a re-import that changes them updates this test.
        List.Exact.Should().HaveCount(258);
        List.Families.Should().HaveCount(118);
    }

    [Fact]
    public void The_firmware_content_is_the_reviewed_one()
    {
        // Counts alone miss a swap; a re-import that changes any string updates this hash after review.
        var text = string.Join("\n", List.Exact.Order(StringComparer.Ordinal));

        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .Should().Be("582a35ab8e0c8e07b824cee63a8cff80c27e7616a024d372f1296bd0e00850ed");
    }

    [Fact]
    public void The_overrides_are_exactly_the_two_the_code_was_reviewed_for()
    {
        // A new family exception is a deliberate, reviewed step: it breaks this test first.
        List.FamilyOverrides.Should().BeEquivalentTo(new Dictionary<string, IReadOnlyDictionary<PerformanceMode, byte>>
        {
            ["15M1IMS2.1"] = new Dictionary<PerformanceMode, byte> { [PerformanceMode.Eco] = 0xC6 },
            ["14D3EMS1.1"] = new Dictionary<PerformanceMode, byte> { [PerformanceMode.High] = 0xC0 },
        });
    }

    [Fact]
    public void The_sources_name_both_projects_and_commits()
    {
        List.Sources.Should().Contain(s => s.StartsWith("msi-ec d7fbbd8,", StringComparison.Ordinal));
        List.Sources.Should().Contain(s => s.StartsWith("GhostDeck 1dbd1ef,", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("15M3EMS1.105", MatchKind.Exact)] // Vector 16 HX AI (issue #3)
    [InlineData("15M3EMS1.199", MatchKind.Family)]
    [InlineData("16Q4EMS2.107", MatchKind.None)]
    public void The_vector_firmware_matches_and_the_p65_does_not(string firmware, MatchKind expected)
    {
        List.Match(new FirmwareInfo(firmware, "")).Should().Be(expected);
    }

    [Fact]
    public void The_p65_firmware_still_goes_to_its_own_record()
    {
        FirmwareMatcher.Match(new FirmwareInfo("16Q4EMS2.107", ""), Catalog)
            .Should().Be(new DeviceMatch(MatchKind.Exact, EmbeddedDevices.P65Id, "MSI P65 Creator 9SE"));
    }

    [Fact]
    public void The_list_is_not_embedded_as_a_device_record()
    {
        typeof(DeviceConfigLoader).Assembly.GetManifestResourceNames()
            .Should().Contain(Wmi2FirmwareList.ResourceName)
            .And.NotContain(n => n.StartsWith(DeviceConfigLoader.ResourcePrefix, StringComparison.Ordinal) && n.Contains("wmi2", StringComparison.Ordinal));
    }
}
