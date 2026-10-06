using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public class P65DeviceTests
{
    [Fact]
    public void Reads_firmware_version_and_date()
    {
        var device = new P65Device(P65Memory.FactorySnapshot(), TestLayouts.P65);

        var firmware = device.ReadFirmware();

        firmware.Should().Be(new FirmwareInfo("16Q4EMS2.107", "05132019"));
        firmware.IsSupported.Should().BeTrue();
    }

    [Theory]
    [InlineData("16Q4EMS2.108")]
    [InlineData("16Q4EMS1.110")]
    [InlineData("")]
    public void Only_the_exact_verified_firmware_is_supported(string version)
    {
        new FirmwareInfo(version, "").IsSupported.Should().BeFalse();
    }

    [Fact]
    public void Firmware_text_stops_at_padding()
    {
        var ec = new FakeEcRegisters();
        ec.LoadAscii(0xA0, "16Q4EMS2.1\0\0");

        new P65Device(ec, TestLayouts.P65).ReadFirmware().Version.Should().Be("16Q4EMS2.1");
    }

    [Fact]
    public void Reads_sensors()
    {
        var device = new P65Device(P65Memory.FactorySnapshot(), TestLayouts.P65);

        var sensors = device.ReadSensors();

        sensors.Should().Be(new SensorSnapshot(
            CpuTempC: 60, GpuTempC: 46, CpuFanPercent: 50, GpuFanPercent: 0, CpuRpm: 3044, GpuRpm: 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(111)]
    [InlineData(255)]
    public void Implausible_temperature_is_reported_as_missing(byte raw)
    {
        var ec = P65Memory.FactorySnapshot();
        ec.Load(0x68, raw);

        new P65Device(ec, TestLayouts.P65).ReadSensors().CpuTempC.Should().BeNull();
    }

    [Fact]
    public void Reads_both_fan_curves()
    {
        var device = new P65Device(P65Memory.FactorySnapshot(), TestLayouts.P65);

        var curves = device.ReadFanCurves();

        curves.Cpu.Points[1].Should().Be(new FanPoint(55, 50));
        curves.Gpu.Points[0].Should().Be(new FanPoint(0, 0));
        curves.Gpu.Points[6].Should().Be(new FanPoint(86, 90));
    }
}
