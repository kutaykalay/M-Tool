using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.Tests.ViewModels;

/// <summary>The device lines of the About tab: only values M-Tool already has, nothing private.</summary>
public sealed class DeviceInfoTests
{
    private static readonly SystemInfo P65System = new(
        "Micro-Star International Co., Ltd.", "P65 Creator 9SE", "MS-16Q4", "E16Q4IMS.10B", "2019-08-21", "Intel(R) Core(TM) i7-9750H", "Windows 11 Home 10.0.26300");

    private static DeviceAccess Access(
        WriteMode mode = WriteMode.Enabled, bool port = true, FirmwareInfo? firmware = null, DeviceMatch? match = null) => new(
        firmware ?? new FirmwareInfo("16Q4EMS2.107", "2019-08-21"), mode, mode == WriteMode.Locked ? "reason" : null, port,
        new DeviceCapabilities(false, false, false, false, []), match ?? new DeviceMatch(MatchKind.Exact, "p65-9se", "MSI P65 Creator 9SE"));

    private static string Value(IReadOnlyList<DeviceInfoLine> lines, string label) => lines.Single(l => l.Label == label).Value;

    [Fact]
    public void Every_line_is_filled_from_what_M_Tool_read()
    {
        var lines = DeviceInfo.Lines(P65System, Access(), new Version(2, 0, 1));

        lines.Select(l => l.Label).Should().Equal(
            Strings.About_Device_Model, Strings.About_Device_Bios, Strings.About_Device_Cpu, Strings.About_Device_Windows,
            Strings.About_Device_Firmware, Strings.About_Device_Record, Strings.About_Device_EcAccess, Strings.About_Device_Writes);
        Value(lines, Strings.About_Device_Model).Should().Be("Micro-Star International Co., Ltd. P65 Creator 9SE");
        Value(lines, Strings.About_Device_Bios).Should().Be("E16Q4IMS.10B (2019-08-21)");
        Value(lines, Strings.About_Device_Cpu).Should().Be("Intel(R) Core(TM) i7-9750H");
        Value(lines, Strings.About_Device_Windows).Should().Be("Windows 11 Home 10.0.26300");
        Value(lines, Strings.About_Device_Firmware).Should().Be("16Q4EMS2.107 (2019-08-21)");
        Value(lines, Strings.About_Device_Record).Should().Be($"MSI P65 Creator 9SE ({Strings.About_Match_Exact})");
        Value(lines, Strings.About_Device_EcAccess).Should().Be(string.Format(Strings.About_Access_WmiAndPort, "2.0.1"));
        Value(lines, Strings.About_Device_Writes).Should().Be(Strings.About_Writes_Enabled);
    }

    [Fact]
    public void What_could_not_be_read_is_unknown()
    {
        var lines = DeviceInfo.Lines(null, Access() with { Firmware = null, Match = null }, null);

        foreach (var label in new[]
                 {
                     Strings.About_Device_Model, Strings.About_Device_Bios, Strings.About_Device_Cpu,
                     Strings.About_Device_Windows, Strings.About_Device_Firmware, Strings.About_Device_Record,
                 })
        {
            Value(lines, label).Should().Be(Strings.About_Unknown, label);
        }
    }

    [Fact]
    public void Blank_and_partial_values_are_unknown_or_shortened()
    {
        var system = new SystemInfo("  ", "P65", null, "E16Q4IMS.10B", null, "", null);

        var lines = DeviceInfo.Lines(system, Access(), null);

        Value(lines, Strings.About_Device_Model).Should().Be("P65");
        Value(lines, Strings.About_Device_Bios).Should().Be("E16Q4IMS.10B");
        Value(lines, Strings.About_Device_Cpu).Should().Be(Strings.About_Unknown);
        Value(lines, Strings.About_Device_Windows).Should().Be(Strings.About_Unknown);
    }

    [Theory]
    [InlineData(MatchKind.Family, "About_Match_Family")]
    [InlineData(MatchKind.None, "About_Match_None")]
    public void A_record_that_is_not_an_exact_match_says_so(MatchKind kind, string key)
    {
        var match = kind == MatchKind.None ? DeviceMatch.None : new DeviceMatch(kind, "p65-9se", "MSI P65 Creator 9SE");

        var record = Value(DeviceInfo.Lines(P65System, Access(match: match), null), Strings.About_Device_Record);

        var text = Strings.ResourceManager.GetString(key)!;
        record.Should().Be(kind == MatchKind.None ? text : $"MSI P65 Creator 9SE ({text})");
    }

    [Theory]
    [InlineData(WriteMode.Enabled, "About_Writes_Enabled")]
    [InlineData(WriteMode.DryRun, "About_Writes_DryRun")]
    [InlineData(WriteMode.Locked, "About_Writes_Locked")]
    public void The_write_mode_is_named(WriteMode mode, string key)
    {
        Value(DeviceInfo.Lines(P65System, Access(mode), null), Strings.About_Device_Writes)
            .Should().Be(Strings.ResourceManager.GetString(key));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Without_the_port_or_a_known_PawnIO_the_access_line_says_so(bool port, bool knownPawnIo)
    {
        var access = Value(DeviceInfo.Lines(P65System, Access(port: port), knownPawnIo ? new Version(2, 0, 1) : null), Strings.About_Device_EcAccess);

        access.Should().Be(port ? string.Format(Strings.About_Access_WmiAndPort, Strings.About_Unknown) : Strings.About_Access_WmiOnly);
    }

    [Fact]
    public void The_copied_text_starts_with_the_version_and_has_one_line_per_value()
    {
        var lines = DeviceInfo.Lines(P65System, Access(), new Version(2, 0, 1));

        var text = DeviceInfo.ToText("0.16.0-beta.1 (448e0a5)", lines);

        text.Split(Environment.NewLine).Should().Equal(
            ["M-Tool 0.16.0-beta.1 (448e0a5)", .. lines.Select(l => $"{l.Label}: {l.Value}")]);
    }

    [Fact]
    public void Control_characters_from_SMBIOS_do_not_reach_the_window()
    {
        var system = P65System with { Model = "P65\r\nCreator\u0007", Cpu = "‮evil" };

        var lines = DeviceInfo.Lines(system, Access(), null);

        string.Concat(lines.Select(l => l.Value)).Should().NotContainAny("\r", "\n", "\u0007", "‮");
        Value(lines, Strings.About_Device_Model).Should().Be("Micro-Star International Co., Ltd. P65 Creator");
    }
}
