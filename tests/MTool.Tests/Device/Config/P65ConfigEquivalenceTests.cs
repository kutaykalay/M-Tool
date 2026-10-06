using MTool.Core.Device.Config;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device.Config;

/// <summary>The embedded P65 record says exactly what the code says today (<see cref="P65Golden"/>).</summary>
public class P65ConfigEquivalenceTests
{
    private readonly DeviceConfig _p65 = DeviceConfigLoader.LoadEmbedded(new ListLog()).Single(c => c.Id == "msi-p65-creator-9se");

    [Fact]
    public void Identity_status_and_interface()
    {
        _p65.SchemaVersion.Should().Be(1);
        _p65.Status.Should().Be(DeviceStatus.WriteVerified);
        _p65.Interface.Should().Be(WmiInterface.Wmi1);
        _p65.Firmware.Exact.Should().Equal(P65Golden.Firmware);
        _p65.Firmware.Family.Should().Be("16Q4EMS2.1");
        _p65.Sources.Should().NotBeEmpty();
    }

    [Fact]
    public void Firmware_location()
    {
        _p65.FirmwareLocation.Should().Be(new FirmwareLocation(
            P65Golden.FirmwareVersion, P65Golden.FirmwareVersionLength, P65Golden.FirmwareDate, P65Golden.FirmwareDateLength));
    }

    [Fact]
    public void Fan_registers()
    {
        _p65.Fans.Select(f => f.Id).Should().Equal("cpu", "gpu");
        Registers(Fan("cpu")).Should().Be(P65Golden.CpuFan);
        Registers(Fan("gpu")).Should().Be(P65Golden.GpuFan);
        Block(Fan("cpu").UpThresholds).Should().Equal(P65Golden.CpuUpThresholds);
        Block(Fan("cpu").Speeds).Should().Equal(P65Golden.CpuSpeeds);
        Block(Fan("gpu").UpThresholds).Should().Equal(P65Golden.GpuUpThresholds);
        Block(Fan("gpu").Speeds).Should().Equal(P65Golden.GpuSpeeds);
    }

    [Fact]
    public void Fan_table_registers()
    {
        FanTables().Order().Should().Equal(P65Golden.FanTableRegisters);
    }

    [Fact]
    public void Feature_registers_and_values()
    {
        var f = _p65.Features;

        f.CoolerBoost.Should().Be(new CoolerBoostFeature(P65Golden.CoolerBoost, 7));
        f.ChargeLimit.Should().Be(new ChargeLimitFeature(
            P65Golden.ChargeLimit, 7, P65Golden.MinChargeLimitPercent, P65Golden.MaxChargeLimitPercent));
        f.FanMode.Should().Be(new FanModeFeature(P65Golden.FanMode,
            P65Golden.FanModes.Single(m => m.Mode == "Auto").Value, P65Golden.FanModes.Single(m => m.Mode == "Advanced").Value));
        f.PerformanceMode!.Register.Should().Be(P65Golden.PerformanceMode);
    }

    [Fact]
    public void Performance_modes_keep_the_enum_order_and_bytes()
    {
        _p65.Features.PerformanceMode!.Modes.Select(m => (m.Id, m.Value))
            .Should().Equal(P65Golden.PerformanceModes.Select(m => (m.Mode.ToLowerInvariant(), m.Value)));
    }

    [Fact]
    public void Writable_registers_derive_to_the_30_golden_ones()
    {
        var f = _p65.Features;
        byte[] features = [f.CoolerBoost!.Register, f.ChargeLimit!.Register, f.PerformanceMode!.Register, f.FanMode!.Register];

        FanTables().Concat(features).Order().Should().Equal(P65Golden.WritableRegisters);
    }

    [Fact]
    public void Port_guard_watch_list_derives_to_the_28_golden_ones()
    {
        FanTables().Append(_p65.Features.PerformanceMode!.Register).Append(_p65.Features.FanMode!.Register).Order()
            .Should().Equal(P65Golden.PortGuardWatched);
    }

    [Fact]
    public void Wmi_fields()
    {
        _p65.Wmi1!.Fields.ToDictionary(f => f.Register, f => (f.Class, f.Index))
            .Should().BeEquivalentTo(P65Golden.WmiFields);
        _p65.Wmi1.Fields.Should().HaveCount(56);
    }

    [Fact]
    public void Port_registers()
    {
        _p65.PortRegisters.Order().Should().Equal(P65Golden.PortRegisters);
    }

    [Fact]
    public void Limits()
    {
        _p65.Limits.Should().Be(new CurveLimits(30, 95, 100));
    }

    [Fact]
    public void Factory_tables_and_down_offsets()
    {
        Points(Fan("cpu").FactoryCurve!).Should().Equal(P65Golden.FactoryCpuCurve);
        Points(Fan("gpu").FactoryCurve!).Should().Equal(P65Golden.FactoryGpuCurve);
        Fan("cpu").FactoryDownOffsets.Should().Equal(P65Golden.CpuDownOffsets);
        Fan("gpu").FactoryDownOffsets.Should().Equal(P65Golden.GpuDownOffsets);
    }

    [Fact]
    public void Presets()
    {
        _p65.Presets.Select(p => p.Name).Should().Equal("Cool", "Silent");
        Points(Preset("Cool").Curves["cpu"]).Should().Equal(P65Golden.CoolCpuCurve);
        Points(Preset("Cool").Curves["gpu"]).Should().Equal(P65Golden.CoolGpuCurve);
        Points(Preset("Silent").Curves["cpu"]).Should().Equal(P65Golden.SilentCpuCurve);
        Points(Preset("Silent").Curves["gpu"]).Should().Equal(P65Golden.SilentGpuCurve);
    }

    private FanConfig Fan(string id) => _p65.Fans.Single(f => f.Id == id);

    private PresetConfig Preset(string name) => _p65.Presets.Single(p => p.Name == name);

    private IEnumerable<byte> FanTables() =>
        _p65.Fans.SelectMany(f => Block(f.UpThresholds).Concat(Block(f.Speeds)));

    private static (byte, byte, byte, byte, byte) Registers(FanConfig fan) =>
        (fan.Temperature, fan.SpeedPercent, fan.RpmHigh, fan.UpThresholds.Start, fan.Speeds.Start);

    private static IEnumerable<byte> Block(RegisterBlock block) =>
        Enumerable.Range(block.Start, block.Count).Select(r => (byte)r);

    private static IEnumerable<(int Up, int Speed)> Points(FanCurve curve) =>
        curve.Points.Select(p => (p.UpThresholdC, p.SpeedPercent));
}
