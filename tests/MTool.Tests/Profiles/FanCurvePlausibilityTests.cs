using MTool.Core.Profiles;

namespace MTool.Tests.Profiles;

/// <summary>Whether a table read from the EC can be a real fan table at all (7f, K13): a wrong map reads nonsense.</summary>
public class FanCurvePlausibilityTests
{
    [Fact]
    public void Every_built_in_table_is_plausible()
    {
        foreach (var profile in ProfileCatalog.BuiltIn.Profiles)
        {
            profile.Curves.Cpu.IsPlausible().Should().BeTrue(profile.Name);
            profile.Curves.Gpu.IsPlausible().Should().BeTrue(profile.Name);
        }
    }

    [Theory]
    [InlineData(55, 55)] // equal thresholds
    [InlineData(64, 50)] // falling thresholds
    public void Thresholds_must_rise(int second, int third)
    {
        FanCurve.Of((0, 45), (50, 50), (second, 60), (third, 70), (76, 75), (82, 80), (88, 80)).IsPlausible().Should().BeFalse();
    }

    [Theory]
    [InlineData(5, 50)] // below any real threshold
    [InlineData(130, 50)] // above any real threshold (the last point, so the rise still holds)
    [InlineData(55, 200)] // a speed no EC uses
    [InlineData(55, -1)]
    public void Values_must_be_in_range(int threshold, int speed)
    {
        // Every other point is a real one: only the value under test can make the table implausible.
        var lastIsTheOne = threshold > 100;
        var curve = lastIsTheOne
            ? FanCurve.Of((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (threshold, speed))
            : FanCurve.Of((0, 45), (threshold, speed), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80));

        curve.IsPlausible().Should().BeFalse();
    }

    [Fact]
    public void The_same_table_with_real_values_is_plausible()
    {
        FanCurve.Of((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (110, 150)).IsPlausible().Should().BeTrue();
    }

    [Fact]
    public void A_curve_without_seven_points_is_not_a_table()
    {
        FanCurve.Of((0, 45), (55, 50)).IsPlausible().Should().BeFalse();
    }
}
