using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device.Config;

/// <summary>The instances the read side now uses equal the literal <see cref="P65Golden"/> tables.</summary>
public class P65LayoutGoldenTests
{
    private static readonly DeviceLayout Layout = TestLayouts.P65;

    [Fact]
    public void Firmware_location_matches()
    {
        Layout.Firmware.Should().Be(new FirmwareLocation(
            P65Golden.FirmwareVersion, P65Golden.FirmwareVersionLength, P65Golden.FirmwareDate, P65Golden.FirmwareDateLength));
    }

    [Fact]
    public void Fan_registers_match()
    {
        Tuple(Layout.CpuFan).Should().Be(P65Golden.CpuFan);
        Tuple(Layout.GpuFan).Should().Be(P65Golden.GpuFan);
    }

    [Fact]
    public void Feature_registers_match()
    {
        Layout.CoolerBoost.Should().Be(P65Golden.CoolerBoost);
        Layout.ChargeLimit.Should().Be(P65Golden.ChargeLimit);
        Layout.PerformanceMode.Should().Be(P65Golden.PerformanceMode);
        Layout.FanMode.Should().Be(P65Golden.FanMode);
    }

    [Fact]
    public void Wmi_fields_are_the_56_golden_ones()
    {
        Layout.Wmi.Fields.ToDictionary(f => f.Key, f => (f.Value.ClassName, f.Value.Index))
            .Should().BeEquivalentTo(P65Golden.WmiFields);
        Layout.Wmi.Fields.Should().HaveCount(56);
    }

    [Fact]
    public void Port_registers_match()
    {
        Layout.Wmi.PortRegisters.Order().Should().Equal(P65Golden.PortRegisters);
    }

    [Fact]
    public void Read_watch_list_is_both_tables_and_the_two_modes()
    {
        Layout.WatchedRegisters.Should().Equal(P65Golden.PortGuardWatched);
    }

    [Fact]
    public void The_maps_cannot_be_widened_through_a_cast()
    {
        var addField = () => ((IDictionary<byte, WmiField>)Layout.Wmi.Fields).Add(0x99, new WmiField("MSI_CPU", 30));
        var addPort = () => ((ISet<byte>)Layout.Wmi.PortRegisters).Add(0x99);

        addField.Should().Throw<NotSupportedException>();
        addPort.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_single_fan_record_has_no_layout_yet()
    {
        var act = () => DeviceLayout.From(Draft());

        act.Should().Throw<ArgumentException>().WithMessage("*gpu*");
    }

    [Fact]
    public void A_record_without_a_wmi1_map_has_no_layout_yet()
    {
        var act = () => DeviceLayout.From(P65() with { Interface = WmiInterface.Wmi2, Wmi1 = null });

        act.Should().Throw<ArgumentException>().WithMessage("*wmi1*");
    }

    [Fact]
    public void A_record_without_a_feature_has_no_layout_yet()
    {
        var act = () => DeviceLayout.From(P65() with { Features = P65().Features with { FanMode = null } });

        act.Should().Throw<ArgumentException>().WithMessage("*fanMode*");
    }

    [Fact]
    public void An_invalid_record_has_no_layout()
    {
        var act = () => DeviceLayout.From(P65() with { PortRegisters = [0x6A] });

        act.Should().Throw<ArgumentException>().WithMessage("*0x6A*");
    }

    private static (byte, byte, byte, byte, byte) Tuple(FanRegisters fan) =>
        (fan.Temperature, fan.SpeedPercent, fan.RpmHigh, fan.UpThresholdsStart, fan.SpeedsStart);
}
