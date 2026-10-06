using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public class WmiMapTests
{
    /// <summary>Rows of the WMI1 table in the EC access decision, read from this laptop's DSDT and checked on hardware.</summary>
    [Theory]
    [InlineData(0xA0, "MSI_Software", 6)]
    [InlineData(0xB3, "MSI_Software", 25)]
    [InlineData(0x68, "MSI_CPU", 1)]
    [InlineData(0x71, "MSI_CPU", 2)]
    [InlineData(0x6A, "MSI_CPU", 5)]
    [InlineData(0x6F, "MSI_CPU", 10)]
    [InlineData(0x72, "MSI_CPU", 11)]
    [InlineData(0x78, "MSI_CPU", 17)]
    [InlineData(0x80, "MSI_VGA", 1)]
    [InlineData(0x89, "MSI_VGA", 2)]
    [InlineData(0x82, "MSI_VGA", 5)]
    [InlineData(0x87, "MSI_VGA", 10)]
    [InlineData(0x8A, "MSI_VGA", 11)]
    [InlineData(0x90, "MSI_VGA", 17)]
    [InlineData(0xCD, "MSI_AP", 2)]
    [InlineData(0xCC, "MSI_AP", 3)]
    [InlineData(0xCB, "MSI_AP", 4)]
    [InlineData(0xCA, "MSI_AP", 5)]
    [InlineData(0xF2, "MSI_System", 7)]
    [InlineData(0xF4, "MSI_System", 9)]
    public void Maps_registers_to_the_verified_wmi_fields(byte register, string className, int index)
    {
        WmiMap.Fields[register].Should().Be(new WmiField(className, index));
    }

    [Fact]
    public void Maps_exactly_firmware_sensors_rpm_tables_and_the_two_modes()
    {
        // 20 firmware bytes + 4 sensors + 4 RPM bytes + 2 x 13 table bytes + 0xF2 + 0xF4.
        WmiMap.Fields.Should().HaveCount(56);
    }

    [Fact]
    public void Firmware_date_follows_the_version_so_one_software_range_covers_both()
    {
        EcMap.FirmwareDate.Should().Be((byte)(EcMap.FirmwareVersion + EcMap.FirmwareVersionLength));
    }

    [Fact]
    public void No_two_registers_share_a_wmi_field()
    {
        WmiMap.Fields.Values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Port_registers_are_only_cooler_boost_and_charge_limit()
    {
        WmiMap.PortRegisters.Should().BeEquivalentTo(new byte[] { 0x98, 0xEF });
        WmiMap.Fields.Keys.Should().NotIntersectWith(WmiMap.PortRegisters);
    }

    [Fact]
    public void Every_writable_register_is_reachable_through_wmi_or_the_port()
    {
        var reachable = WmiMap.Fields.Keys.Concat(WmiMap.PortRegisters);

        EcWriteRules.WritableRegisters.Should().BeSubsetOf(reachable);
        EcWriteRules.WritableRegisters.Except(WmiMap.PortRegisters).Should().BeSubsetOf(WmiMap.Fields.Keys);
    }

    [Fact]
    public void Every_register_the_device_reads_is_reachable_through_wmi_or_the_port()
    {
        var ec = new RecordingRegisters(P65Memory.FactorySnapshot());
        var device = new P65Device(ec, TestLayouts.P65);

        device.ReadFirmware();
        device.ReadSensors();
        device.ReadFanCurves();
        device.ReadControlState();
        device.ReadPortState();

        ec.Registers.Should().BeSubsetOf(WmiMap.Fields.Keys.Concat(WmiMap.PortRegisters));
    }

    [Fact]
    public void Everything_but_the_port_state_is_read_through_wmi()
    {
        var ec = new RecordingRegisters(P65Memory.FactorySnapshot());
        var device = new P65Device(ec, TestLayouts.P65);

        device.ReadFirmware();
        device.ReadSensors();
        device.ReadControlState();

        ec.Registers.Should().BeSubsetOf(WmiMap.Fields.Keys);
    }

    [Fact]
    public void Fake_wmi_reads_and_writes_the_mapped_ec_memory()
    {
        var memory = P65Memory.FactorySnapshot();
        var wmi = new FakeWmiFields(memory);

        wmi.Read("MSI_CPU", [5, 11]).Should().Equal(55, 45);
        wmi.Write("MSI_System", 7, 0xC1);

        memory[0xF2].Should().Be(0xC1);
        wmi.Calls.Should().Equal("read MSI_CPU[5,11]", "write MSI_System[7]=0xC1");
    }

    [Fact]
    public void Fake_wmi_rejects_fields_that_are_not_in_the_map()
    {
        var wmi = new FakeWmiFields(P65Memory.FactorySnapshot());

        var act = () => wmi.Read("MSI_CPU", [0]);

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class RecordingRegisters(IEcRegisters inner) : IEcRegisters
    {
        public HashSet<byte> Registers { get; } = [];

        public byte Read(byte register)
        {
            Registers.Add(register);
            return inner.Read(register);
        }

        public IReadOnlyList<byte> ReadBlock(byte startRegister, int count)
        {
            for (var i = 0; i < count; i++)
            {
                Registers.Add((byte)(startRegister + i));
            }

            return inner.ReadBlock(startRegister, count);
        }
    }
}
