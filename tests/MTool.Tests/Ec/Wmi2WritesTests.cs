using System.Reflection;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Ec;

/// <summary>
/// The type gate of WMI2 writes (plan asama15 15c-2): a <c>Set_Data</c> request exists only for a register
/// and a byte inside the code's ceiling, whatever the firmware list says.
/// </summary>
public class Wmi2WritesTests
{
    private static readonly Wmi2FirmwareList List = Wmi2FirmwareList.Load(
        """
        {
          "schemaVersion": 1,
          "sources": ["msi-ec d7fbbd8"],
          "exact": ["15M3EMS1.105", "15M1IMS2.105", "14D3EMS1.116"],
          "families": ["15M3EMS1.1", "15M1IMS2.1", "14D3EMS1.1"],
          "familyOverrides": {
            "15M1IMS2.1": { "eco": "0xC6" },
            "14D3EMS1.1": { "high": "0xC0" }
          }
        }
        """,
        [P65()]);

    [Theory]
    [InlineData(0x98, 0x80)]
    [InlineData(0x98, 0x7F)]
    [InlineData(0xD2, 0xC0)]
    [InlineData(0xD2, 0xC1)]
    [InlineData(0xD2, 0xC2)]
    [InlineData(0xD2, 0xC4)]
    [InlineData(0xD2, 0xC6)]
    [InlineData(0xD4, 0x0D)]
    [InlineData(0xD4, 0x1D)]
    [InlineData(0xD7, 0x8A)] // 10 %: written back only
    [InlineData(0xD7, 0xB1)] // 49 %: written back only
    [InlineData(0xD7, 0xB2)] // 50 %
    [InlineData(0xD7, 0xE4)] // 100 %
    public void A_request_inside_the_ceiling_is_made(byte register, byte value)
    {
        var request = Wmi2Writes.Request(register, value);

        request.Register.Should().Be(register);
        request.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(0x6A)]
    [InlineData(0xA0)]
    [InlineData(0xF2)]
    [InlineData(0xEB)] // Super Battery: deferred
    [InlineData(0xEF)] // a port register on the P65
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void A_register_outside_the_ceiling_has_no_request(byte register)
    {
        var request = () => Wmi2Writes.Request(register, 0x0F);

        request.Should().Throw<InvalidOperationException>().WithMessage($"*0x{register:X2}*");
    }

    [Theory]
    [InlineData(0xD2, 0x00)]
    [InlineData(0xD2, 0xC5)] // MSI Center's fourth mode: read, never written
    [InlineData(0xD2, 0xC3)]
    [InlineData(0xD4, 0x8D)] // advanced: a custom curve
    [InlineData(0xD4, 0x9D)]
    [InlineData(0xD4, 0x00)]
    [InlineData(0xD7, 0x00)]
    [InlineData(0xD7, 0x50)] // 80 % without the enable bit
    [InlineData(0xD7, 0x89)] // 9 %
    [InlineData(0xD7, 0xE5)] // 101 %
    [InlineData(0xD7, 0xFF)]
    public void A_byte_outside_the_ceiling_has_no_request(byte register, byte value)
    {
        var request = () => Wmi2Writes.Request(register, value);

        request.Should().Throw<InvalidOperationException>().WithMessage($"*0x{register:X2}*0x{value:X2}*");
    }

    [Fact]
    public void Every_write_the_rules_let_through_is_inside_the_ceiling()
    {
        foreach (var firmware in new[] { "15M3EMS1.105", "15M1IMS2.105", "14D3EMS1.116" })
        {
            foreach (var register in Wmi2WriteCeiling.Registers)
            {
                for (var value = 0; value <= byte.MaxValue; value++)
                {
                    for (var current = 0; current <= byte.MaxValue; current++)
                    {
                        var write = new RegisterWrite(register, (byte)value);
                        var info = new FirmwareInfo(firmware, "");
                        if (Wmi2WriteRules.Check(write, (byte)current, List, info) is null
                            || Wmi2WriteRules.CheckRestore(write, (byte)current, List, info) is null)
                        {
                            Wmi2Writes.IsWithinCeiling(register, (byte)value).Should().BeTrue($"0x{register:X2} = 0x{value:X2} on {firmware}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Every_charge_limit_the_layout_reads_can_be_written_back_and_nothing_more()
    {
        for (var value = 0; value <= byte.MaxValue; value++)
        {
            Wmi2WriteCeiling.IsRestorableChargeLimitByte((byte)value).Should()
                .Be(Wmi2LayoutCheck.Fits(Wmi2WriteCeiling.ChargeLimitRegister, (byte)value), $"0x{value:X2}");
        }
    }

    [Fact]
    public void A_request_cannot_be_made_outside_the_gate()
    {
        // No public or protected constructor, and no setter or init accessor that a "with" could use.
        typeof(Wmi2WriteRequest).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().NotContain(c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly);
        typeof(Wmi2WriteRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should().OnlyContain(p => p.SetMethod == null);
        typeof(Wmi2WriteRequest).Should().BeSealed();
        typeof(Wmi2WriteRequest).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().OnlyContain(c => c.IsPrivate, "not even code in MTool.Core may skip the ceiling");
    }

    [Fact]
    public void The_method_name_is_fixed()
    {
        Wmi2Writes.SetData.Should().Be("Set_Data");
    }

    [Fact]
    public void The_read_interface_has_no_write_member()
    {
        typeof(IWmi2Methods).GetMethods().Should().ContainSingle().Which.Name.Should().Be(nameof(IWmi2Methods.Call));
        typeof(IWmi2Methods).GetMethods().Single().GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be<Wmi2Request>();
    }

    [Fact]
    public void The_write_interface_takes_only_a_gated_request()
    {
        typeof(IWmi2DataWriter).GetMethods().Should().ContainSingle().Which.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be<Wmi2WriteRequest>();
    }
}
