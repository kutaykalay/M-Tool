using System.Text.Json;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device.Config;

/// <summary>
/// What the WMI2 window tells about the msi-ec list (plan asama15 15b-4): only on a WMI2 laptop, and a
/// list that cannot be loaded counts as no list at all, never as a partial or permissive one.
/// </summary>
public class Wmi2SupportTests
{
    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.Load(
        """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105", "15M1IMS2.105"],
          "families": ["15M3EMS1.1", "15M1IMS2.1"],
          "familyOverrides": { "15M1IMS2.1": { "eco": "0xC6" } }
        }
        """,
        [P65()]);

    [Theory]
    [InlineData("15M3EMS1.105", Wmi2ListMatch.Exact)]
    [InlineData("15M3EMS1.199", Wmi2ListMatch.Family)]
    [InlineData("14F1EMS1.112", Wmi2ListMatch.None)]
    [InlineData("", Wmi2ListMatch.None)]
    [InlineData(null, Wmi2ListMatch.None)]
    public void A_wmi2_laptop_gets_its_match(string? firmware, Wmi2ListMatch expected)
    {
        Wmi2Support.Match(WmiInterface.Wmi2, List, firmware).Should().Be(expected);
    }

    [Theory]
    [InlineData(WmiInterface.Wmi1)]
    [InlineData(null)]
    public void Only_a_wmi2_laptop_is_matched(WmiInterface? detected)
    {
        Wmi2Support.Match(detected, List, "15M3EMS1.105").Should().Be(Wmi2ListMatch.None);
    }

    [Theory]
    [InlineData("15M3EMS1.105")]
    [InlineData(null)]
    public void A_list_that_did_not_load_is_unavailable(string? firmware)
    {
        Wmi2Support.Match(WmiInterface.Wmi2, null, firmware).Should().Be(Wmi2ListMatch.Unavailable);
    }

    [Fact]
    public void A_wmi2_laptop_reads_its_family_override()
    {
        Wmi2Support.Performance(WmiInterface.Wmi2, List, "15M1IMS2.105", 0xC6).Should().Be(PerformanceMode.Eco);
        Wmi2Support.Performance(WmiInterface.Wmi2, List, "15M1IMS2.105", 0xC1).Should().Be(PerformanceMode.Balanced);
    }

    [Theory]
    [InlineData(WmiInterface.Wmi1)]
    [InlineData(null)]
    public void Elsewhere_only_standard_bytes_resolve(WmiInterface? detected)
    {
        Wmi2Support.Performance(detected, List, "15M1IMS2.105", 0xC6).Should().BeNull();
        Wmi2Support.Performance(detected, List, "15M1IMS2.105", 0xC2).Should().Be(PerformanceMode.Eco);
    }

    [Fact]
    public void Without_a_list_only_standard_bytes_resolve()
    {
        Wmi2Support.Performance(WmiInterface.Wmi2, null, "15M1IMS2.105", 0xC6).Should().BeNull();
        Wmi2Support.Performance(WmiInterface.Wmi2, null, "15M1IMS2.105", 0xC4).Should().Be(PerformanceMode.High);
    }

    [Fact]
    public void A_list_that_loads_is_returned()
    {
        var log = new ListLog();

        Wmi2Support.TryLoad(() => List, log).Should().BeSameAs(List);
        log.Lines.Should().BeEmpty();
    }

    public static TheoryData<Exception> LoadFailures => new()
    {
        new InvalidDataException("rule broken"),
        new JsonException("not json"),
        new IOException("resource unreadable"),
    };

    [Theory]
    [MemberData(nameof(LoadFailures))]
    public void A_list_that_fails_to_load_is_logged_and_ignored(Exception failure)
    {
        var log = new ListLog();

        Wmi2Support.TryLoad(() => throw failure, log).Should().BeNull();
        log.Lines.Should().ContainSingle().Which.Should().StartWith("ERROR").And.Contain("WMI2 firmware list");
    }

    [Fact]
    public void Any_other_load_failure_is_logged_and_ignored_too()
    {
        var log = new ListLog();

        Wmi2Support.TryLoad(() => throw new ArgumentException("duplicate id"), log).Should().BeNull();
        log.Lines.Should().ContainSingle().Which.Should().StartWith("ERROR");
    }

    [Fact]
    public void Running_out_of_memory_is_not_hidden()
    {
        var act = () => Wmi2Support.TryLoad(() => throw new OutOfMemoryException(), new ListLog());

        act.Should().Throw<OutOfMemoryException>();
    }

    [Fact]
    public void The_embedded_list_loads()
    {
        var log = new ListLog();

        Wmi2Support.LoadEmbedded(log).Should().NotBeNull();
        log.Lines.Where(l => l.StartsWith("ERROR", StringComparison.Ordinal)).Should().BeEmpty();
    }
}
