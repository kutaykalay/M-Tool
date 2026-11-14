using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Tests.Ec;

/// <summary>
/// The once-per-session check that a WMI2 laptop holds the bytes its record expects (plan asama15 §4.4):
/// one register outside them closes every write of the session. 0xD7 = 0x00 does not fit (Kutay, 2026-10-24).
/// </summary>
public class Wmi2LayoutCheckTests
{
    private static Wmi2Status Status(byte boost = 0x0A, byte performance = 0xC1, byte fanMode = 0x0D, byte charge = 0xE4) =>
        new(boost, performance, fanMode, charge);

    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xC1)]
    [InlineData(0xC2)]
    [InlineData(0xC4)]
    [InlineData(0xC5)] // MSI Center's fourth mode: read, never written
    [InlineData(0xC6)]
    public void The_performance_bytes_seen_on_wmi2_laptops_fit(byte value)
    {
        Wmi2LayoutCheck.Fits(0xD2, value).Should().BeTrue();
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xC3)]
    [InlineData(0xC7)]
    [InlineData(0x0D)]
    [InlineData(0xFF)]
    public void Other_performance_bytes_do_not_fit(byte value)
    {
        Wmi2LayoutCheck.Fits(0xD2, value).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x0D)]
    [InlineData(0x1D)]
    [InlineData(0x8D)]
    [InlineData(0x9D)]
    public void Auto_silent_and_both_advanced_fan_modes_fit(byte value)
    {
        Wmi2LayoutCheck.Fits(0xD4, value).Should().BeTrue();
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x0C)]
    [InlineData(0x2D)]
    [InlineData(0x8C)]
    [InlineData(0xFF)]
    public void Other_fan_modes_do_not_fit(byte value)
    {
        Wmi2LayoutCheck.Fits(0xD4, value).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x8A)] // 10 %
    [InlineData(0xD0)] // 80 %
    [InlineData(0xE4)] // 100 %, what MSI Center sets
    public void A_charge_limit_with_the_enable_bit_and_10_to_100_percent_fits(byte value)
    {
        Wmi2LayoutCheck.Fits(0xD7, value).Should().BeTrue();
    }

    [Theory]
    [InlineData(0x00)] // never seen; its meaning is unknown
    [InlineData(0x80)]
    [InlineData(0x89)] // 9 %
    [InlineData(0xE5)] // 101 %
    [InlineData(0x50)] // 80 without the enable bit
    [InlineData(0xFF)]
    public void Other_charge_bytes_do_not_fit(byte value)
    {
        Wmi2LayoutCheck.Fits(0xD7, value).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x0A)]
    [InlineData(0x8B)]
    [InlineData(0xFF)]
    public void Any_cooler_boost_byte_fits_because_its_low_bits_change_by_themselves(byte value)
    {
        Wmi2LayoutCheck.Fits(0x98, value).Should().BeTrue();
    }

    [Theory]
    [InlineData(0xEB)]
    [InlineData(0x6A)]
    [InlineData(0xF2)]
    [InlineData(0x00)]
    public void A_register_outside_the_ceiling_never_fits(byte register)
    {
        Wmi2LayoutCheck.Fits(register, 0xC1).Should().BeFalse();
    }

    [Fact]
    public void A_laptop_holding_the_expected_bytes_is_verified()
    {
        var result = Wmi2LayoutCheck.Check(Status());

        result.Verified.Should().BeTrue();
        result.Mismatches.Should().BeEmpty();
    }

    public static TheoryData<Wmi2Status, byte> OneOutOfPlace => new()
    {
        { Status(performance: 0x00), 0xD2 },
        { Status(fanMode: 0x00), 0xD4 },
        { Status(charge: 0x00), 0xD7 },
    };

    [Theory]
    [MemberData(nameof(OneOutOfPlace))]
    public void One_register_outside_the_layout_fails_the_whole_check(Wmi2Status status, byte register)
    {
        var result = Wmi2LayoutCheck.Check(status);

        result.Verified.Should().BeFalse();
        result.Mismatches.Should().Equal(new Wmi2LayoutMismatch(register, 0x00));
    }

    [Fact]
    public void A_missing_status_or_list_is_refused()
    {
        var check = () => Wmi2LayoutCheck.Check(null!);
        var noStatus = () => new Wmi2LayoutResult(null!, []);
        var noList = () => new Wmi2LayoutResult(new Wmi2Status(0, 0xC1, 0x0D, 0xE4), null!);

        check.Should().Throw<ArgumentNullException>();
        noStatus.Should().Throw<ArgumentNullException>();
        noList.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Every_mismatch_is_listed_in_register_order_with_its_raw_byte()
    {
        var result = Wmi2LayoutCheck.Check(Status(performance: 0xC3, fanMode: 0x2D, charge: 0x00));

        result.Mismatches.Should().Equal(
            new Wmi2LayoutMismatch(0xD2, 0xC3),
            new Wmi2LayoutMismatch(0xD4, 0x2D),
            new Wmi2LayoutMismatch(0xD7, 0x00));
        result.ToString().Should().Be("0xD2=0xC3, 0xD4=0x2D, 0xD7=0x00");
    }

    [Fact]
    public void A_verified_check_says_so_for_the_log()
    {
        Wmi2LayoutCheck.Check(Status()).ToString().Should().Be("verified");
    }
}
