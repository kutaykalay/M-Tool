using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

/// <summary>The WMI2 (B family) sensor and status registers, read and decoded; unverified, read-only.</summary>
public class Wmi2FamilyTests
{
    private static FakeWmi2Methods Wmi(params (byte Register, byte Value)[] values)
    {
        var wmi = new FakeWmi2Methods();
        foreach (var (register, value) in values)
        {
            wmi[register] = value;
        }

        return wmi;
    }

    [Fact]
    public void Every_register_the_family_reads_is_pinned()
    {
        // Two independent pins of the same map: the family's reads and the allow list must agree.
        Wmi2Family.SensorRegisters.Should().Equal(Wmi2Calls.SensorRegisters);
        Wmi2Family.StatusRegisters.Should().Equal(Wmi2Calls.StatusRegisters);
    }

    [Fact]
    public void Sensors_are_the_live_list_in_order()
    {
        var wmi = Wmi((0x68, 61), (0x80, 48), (0x71, 45), (0x89, 30), (0xC8, 0x00), (0xC9, 0xBC), (0xCA, 0x01), (0xCB, 0x20));

        var snapshot = Wmi2Family.ReadSensors(new Wmi2EcRegisters(wmi));

        wmi.Calls.Should().Equal(Wmi2Calls.Live);
        snapshot.Should().Be(new SensorSnapshot(61, 48, 45, 30, 2542, 1659, new Wmi2Tach(0x00, 0xBC, 0x01, 0x20)));
    }

    [Fact]
    public void An_unclear_tach_reads_as_zero_rpm_and_keeps_its_raw_bytes()
    {
        var wmi = Wmi((0x68, 0), (0x80, 0), (0xC8, 0x01), (0xC9, 0x41));

        var snapshot = Wmi2Family.ReadSensors(new Wmi2EcRegisters(wmi));

        snapshot.CpuTempC.Should().BeNull("0 °C is no reading (a sleeping GPU reads 0)");
        snapshot.CpuRpm.Should().Be(0);
        snapshot.Tach.Should().Be(new Wmi2Tach(0x01, 0x41, 0x00, 0x00));
        Wmi2Rpm.Resolve(snapshot.Tach!.Value.CpuHigh, snapshot.Tach.Value.CpuLow).Should().BeNull();
    }

    [Fact]
    public void Status_is_the_status_list_in_order()
    {
        var wmi = Wmi((0x98, 0x80), (0xD2, 0xC4), (0xD4, 0x1D), (0xD7, 0xD0));

        var status = Wmi2Family.ReadStatus(new Wmi2EcRegisters(wmi));

        wmi.Calls.Should().Equal(Wmi2Calls.Status);
        status.Should().Be(new Wmi2Status(CoolerBoostRaw: 0x80, PerformanceRaw: 0xC4, FanModeRaw: 0x1D, ChargeRaw: 0xD0));
    }

    [Theory]
    [InlineData(0xC1, PerformanceMode.Balanced)]
    [InlineData(0xC2, PerformanceMode.Eco)]
    [InlineData(0xC4, PerformanceMode.High)]
    [InlineData(0xC0, null)] // turbo on one model only
    [InlineData(0xC5, null)]
    [InlineData(0x80, null)]
    public void Performance_values_have_a_name_only_where_the_tested_models_agree(byte raw, PerformanceMode? mode)
    {
        Status(performance: raw).Performance.Should().Be(mode);
    }

    [Theory]
    [InlineData(0x0D, Wmi2FanMode.Auto)]
    [InlineData(0x1D, Wmi2FanMode.Silent)]
    [InlineData(0x8D, Wmi2FanMode.Advanced)]
    [InlineData(0x9D, Wmi2FanMode.Advanced)] // bit 7 is the signal (GhostDeck, 15M1IMS2)
    [InlineData(0x4D, null)]
    [InlineData(0x00, null)]
    public void Fan_mode_values_have_a_name_only_where_the_tested_models_agree(byte raw, Wmi2FanMode? mode)
    {
        Status(fanMode: raw).FanMode.Should().Be(mode);
    }

    [Theory]
    [InlineData(0x80, true)]
    [InlineData(0x81, true)]
    [InlineData(0x00, false)]
    [InlineData(0x7F, false)]
    public void Cooler_boost_is_bit_7(byte raw, bool on)
    {
        Status(coolerBoost: raw).CoolerBoost.Should().Be(on);
    }

    [Theory]
    [InlineData(0xD0, 80)]
    [InlineData(0xE4, 100)]
    [InlineData(0x8A, 10)]
    [InlineData(0x50, null)] // limit off
    [InlineData(0x89, null)] // below 10 %
    [InlineData(0xE5, null)] // above 100 %
    public void The_charge_limit_is_the_low_7_bits_when_bit_7_is_set(byte raw, int? percent)
    {
        Status(charge: raw).ChargeLimitPercent.Should().Be(percent);
    }

    private static Wmi2Status Status(byte coolerBoost = 0, byte performance = 0xC1, byte fanMode = 0x0D, byte charge = 0) =>
        new(coolerBoost, performance, fanMode, charge);
}
