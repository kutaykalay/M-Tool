using MTool.Core.Device;

namespace MTool.Tests.Device;

/// <summary>
/// The tach divisor on WMI2 models is a single byte (low) or a big-endian pair (high:low), by model,
/// and the two agree whenever the high byte is 0 (docs/plans/asama10.md §1.6).
/// </summary>
public class Wmi2RpmTests
{
    [Theory]
    [InlineData(0x00, 0x00, 0)] // stopped
    [InlineData(0x00, 0xBC, 2542)] // 478000 / 188, both readings agree
    [InlineData(0x00, 0x3C, 7966)] // 478000 / 60, still a fan speed
    public void With_a_zero_high_byte_the_low_byte_is_the_divisor(byte high, byte low, int rpm)
    {
        Wmi2Rpm.Resolve(high, low).Should().Be(rpm);
    }

    [Theory]
    [InlineData(0x00, 0x3B)] // 478000 / 59 = 8101
    [InlineData(0x00, 0x01)] // 478000
    public void Faster_than_any_laptop_fan_is_no_reading(byte high, byte low)
    {
        Wmi2Rpm.Resolve(high, low).Should().BeNull();
    }

    [Theory]
    [InlineData(0x01, 0x20, 1659)] // pair 288; one byte would be 478000 / 32 = 14937
    [InlineData(0x01, 0x3B, 1517)] // pair 315; one byte would be 8101
    public void A_one_byte_reading_too_fast_to_be_real_means_the_pair(byte high, byte low, int rpm)
    {
        Wmi2Rpm.Resolve(high, low).Should().Be(rpm);
    }

    [Theory]
    [InlineData(0x01, 0x41)] // pair 1489 rpm or one byte 7353 rpm (GhostDeck #205)
    [InlineData(0x01, 0x00)] // pair 1867 rpm or one byte stopped
    [InlineData(0x05, 0xC8)] // pair 320 rpm or one byte 2390 rpm
    public void When_both_readings_are_possible_there_is_no_reading(byte high, byte low)
    {
        Wmi2Rpm.Resolve(high, low).Should().BeNull();
    }
}
