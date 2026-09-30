using MTool.Core.Device;
using MTool.Core.Profiles;

namespace MTool.Tests.Profiles;

public class CurveValidatorTests
{
    private static FanCurve FactoryCpu => FactoryDefaults.FanCurves.Cpu;

    private static FanCurve With(FanCurve curve, int index, FanPoint point)
    {
        var points = curve.Points.ToArray();
        points[index] = point;
        return new FanCurve(points);
    }

    [Fact]
    public void Factory_and_preset_curves_are_valid()
    {
        var curves = new[]
        {
            FactoryDefaults.FanCurves.Cpu, FactoryDefaults.FanCurves.Gpu,
            Presets.Cool.Curves.Cpu, Presets.Cool.Curves.Gpu,
            Presets.Silent.Curves.Cpu, Presets.Silent.Curves.Gpu,
        };

        curves.Should().AllSatisfy(c => CurveValidator.Validate(c).Should().BeEmpty());
    }

    [Fact]
    public void Requires_seven_points()
    {
        var curve = new FanCurve(FactoryCpu.Points.Take(6).ToArray());

        CurveValidator.Validate(curve).Should().ContainSingle().Which.Should().Contain("7");
    }

    [Fact]
    public void Up_thresholds_must_strictly_increase()
    {
        var curve = With(FactoryCpu, 3, new FanPoint(64, 62, 70));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("artan"));
    }

    [Fact]
    public void Speeds_must_not_decrease()
    {
        var curve = With(FactoryCpu, 3, new FanPoint(70, 67, 55));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("hız"));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(96)]
    public void Up_thresholds_stay_in_range(int up)
    {
        var index = up < 50 ? 1 : 6;
        var curve = With(FactoryCpu, index, new FanPoint(up, up - 3, FactoryCpu.Points[index].SpeedPercent));

        CurveValidator.Validate(curve).Should().NotBeEmpty();
    }

    [Fact]
    public void Speed_cannot_exceed_one_hundred_percent()
    {
        var curve = With(FactoryCpu, 6, new FanPoint(88, 85, 101));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("100"));
    }

    [Theory]
    [InlineData(64)] // equal to up: no hysteresis
    [InlineData(55)] // not above the previous step's up threshold
    public void Down_threshold_sits_between_previous_and_own_up_threshold(int down)
    {
        var curve = With(FactoryCpu, 2, new FanPoint(64, down, 60));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("aşağı"));
    }

    [Fact]
    public void Hysteresis_offset_is_at_most_fifteen_degrees()
    {
        var curve = With(FactoryCpu, 1, new FanPoint(55, 39, 50));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("15"));
    }

    [Fact]
    public void Last_step_must_start_at_or_below_ninety_degrees()
    {
        var curve = With(FactoryCpu, 6, new FanPoint(91, 88, 80));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("90"));
    }

    [Fact]
    public void Last_step_must_run_at_least_eighty_percent()
    {
        var curve = With(FactoryCpu, 6, new FanPoint(88, 85, 79));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("80"));
    }

    [Fact]
    public void First_point_carries_no_thresholds()
    {
        var curve = With(FactoryCpu, 0, new FanPoint(40, 35, 45));

        CurveValidator.Validate(curve).Should().NotBeEmpty();
    }

    [Fact]
    public void A_fan_that_stays_off_until_high_temperatures_is_rejected()
    {
        var curve = FanCurve.Of((0, 0, 0), (84, 81, 0), (85, 83, 0), (86, 85, 0), (87, 86, 0), (88, 87, 0), (90, 89, 80));

        CurveValidator.Validate(curve).Should().NotBeEmpty();
    }

    [Fact]
    public void The_first_step_must_start_by_sixty_five_degrees()
    {
        var curve = FanCurve.Of((0, 0, 40), (66, 58, 50), (70, 67, 60), (74, 71, 70), (78, 75, 75), (82, 79, 80), (88, 85, 80));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("65"));
    }

    [Fact]
    public void The_fan_must_reach_half_speed_by_seventy_five_degrees()
    {
        var curve = FanCurve.Of((0, 0, 20), (60, 52, 30), (68, 65, 40), (75, 72, 45), (80, 77, 70), (85, 82, 80), (90, 87, 100));

        CurveValidator.Validate(curve).Should().Contain(e => e.Contains("75"));
    }
}
