using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public class P65DeviceTests
{
    private static FakeEcRegisters Faz0Snapshot()
    {
        var ec = new FakeEcRegisters();
        ec.LoadAscii(0xA0, "16Q4EMS2.107");
        ec.LoadAscii(0xAC, "05132019");
        ec.Load(0x68, 60);
        ec.Load(0x71, 50);
        ec.Load(0x80, 46);
        ec.Load(0x89, 0);
        ec.Load(0xCA, 0, 0);
        ec.Load(0xCC, 0, 157);
        ec.Load(0x6A, 55, 64, 70, 76, 82, 88);
        ec.Load(0x72, 45, 50, 60, 70, 75, 80, 80);
        ec.Load(0x7A, 8, 3, 3, 3, 3, 3);
        ec.Load(0x82, 55, 61, 65, 71, 77, 86);
        ec.Load(0x8A, 0, 50, 60, 70, 80, 90, 90);
        ec.Load(0x92, 8, 3, 3, 3, 3, 5);
        return ec;
    }

    [Fact]
    public void Reads_firmware_version_and_date()
    {
        var device = new P65Device(Faz0Snapshot());

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

        new P65Device(ec).ReadFirmware().Version.Should().Be("16Q4EMS2.1");
    }

    [Fact]
    public void Reads_sensors()
    {
        var device = new P65Device(Faz0Snapshot());

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
        var ec = Faz0Snapshot();
        ec.Load(0x68, raw);

        new P65Device(ec).ReadSensors().CpuTempC.Should().BeNull();
    }

    [Fact]
    public void Reads_both_fan_curves()
    {
        var device = new P65Device(Faz0Snapshot());

        var curves = device.ReadFanCurves();

        curves.Cpu.Points[1].Should().Be(new FanPoint(55, 47, 50));
        curves.Gpu.Points[0].Should().Be(new FanPoint(0, 0, 0));
        curves.Gpu.Points[6].Should().Be(new FanPoint(86, 81, 90));
    }
}
