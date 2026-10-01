using MTool.Core.Ec;

namespace MTool.Tests.Ec;

public class EcWriteRulesTests
{
    [Theory]
    [InlineData(0x7A)]
    [InlineData(0x7F)]
    [InlineData(0x92)]
    [InlineData(0x97)]
    public void Down_offset_registers_are_not_writable(byte register)
    {
        EcWriteRules.CheckStatic(new RegisterWrite(register, 3)).Should().Contain("listesinde değil");
    }

    [Fact]
    public void Whitelist_is_thresholds_speeds_and_the_four_settings()
    {
        byte[] expected =
        [
            0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
            0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 0x90,
            0x98, 0xEF, 0xF2, 0xF4,
        ];

        EcWriteRules.WritableRegisters.Should().Equal(expected);
    }
}
