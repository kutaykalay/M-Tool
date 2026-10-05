using MTool.Core.Device;
using MTool.Core.Profiles;

namespace MTool.Tests.Profiles;

public class CurveValidatorTests
{
    private static FanCurve FactoryCpu => FactoryDefaults.FanCurves.Cpu;

    private static IReadOnlyList<int> CpuOffsets => FactoryDefaults.CpuDownOffsets;

    private static FanCurve With(FanCurve curve, int index, FanPoint point)
    {
        var points = curve.Points.ToArray();
        points[index] = point;
        return new FanCurve(points);
    }

    private static IReadOnlyList<string> ValidateCpu(FanCurve curve) => CurveValidator.Validate(curve, CpuOffsets);

    [Fact]
    public void Factory_and_preset_curves_are_valid_with_the_factory_down_offsets()
    {
        var profiles = new[] { FactoryDefaults.Profile, Presets.Cool, Presets.Silent };

        profiles.Should().AllSatisfy(p =>
        {
            CurveValidator.Validate(p.Curves.Cpu, FactoryDefaults.CpuDownOffsets).Should().BeEmpty(p.Name);
            CurveValidator.Validate(p.Curves.Gpu, FactoryDefaults.GpuDownOffsets).Should().BeEmpty(p.Name);
        });
    }

    [Fact]
    public void Both_fans_are_checked_with_their_factory_offsets_and_labelled()
    {
        // 87 - 5 = 82 is not above 83 with the GPU's last offset; with the CPU's 3 (84) it passes.
        var gpu = FanCurve.Of((0, 0), (60, 40), (67, 50), (73, 60), (78, 70), (83, 85), (87, 100));
        var cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 70), (85, 70), (90, 70));

        var errors = CurveValidator.ValidateWithFactoryOffsets(new FanCurves(cpu, gpu));

        errors.Should().HaveCount(2);
        errors[0].Should().StartWith("CPU: Güvenlik tabanı");
        errors[1].Should().StartWith("GPU: Adım 6");
        CurveValidator.ValidateWithFactoryOffsets(FactoryDefaults.FanCurves).Should().BeEmpty();
        CurveValidator.ValidateWithFactoryOffsets(new FanCurves(gpu, FactoryDefaults.FanCurves.Gpu)).Should().BeEmpty();
    }

    [Fact]
    public void A_curve_with_no_point_at_or_below_the_envelope_temperature_is_reported_not_thrown()
    {
        var curve = FanCurve.Of((100, 100), (100, 100), (100, 100), (100, 100), (100, 100), (100, 100), (100, 100));

        var act = () => ValidateCpu(curve);

        act.Should().NotThrow().Which.Should().NotBeEmpty();
    }

    [Fact]
    public void Validation_never_throws_on_any_seven_point_curve()
    {
        var random = new Random(20261005);
        for (var run = 0; run < 20_000; run++)
        {
            var curve = new FanCurve([.. Enumerable.Range(0, CurveValidator.PointCount)
                .Select(_ => new FanPoint(random.Next(-300, 300), random.Next(-300, 300)))]);

            var act = () => ValidateCpu(curve);

            act.Should().NotThrow($"curve {string.Join(", ", curve.Points)}");
        }
    }

    [Fact]
    public void Factory_down_offsets_are_the_ones_read_from_the_laptop()
    {
        FactoryDefaults.CpuDownOffsets.Should().Equal(8, 3, 3, 3, 3, 3);
        FactoryDefaults.GpuDownOffsets.Should().Equal(8, 3, 3, 3, 3, 5);
    }

    [Fact]
    public void Requires_seven_points()
    {
        var curve = new FanCurve(FactoryCpu.Points.Take(6).ToArray());

        ValidateCpu(curve).Should().ContainSingle().Which.Should().Contain("7");
    }

    [Fact]
    public void Requires_one_down_offset_per_step()
    {
        var act = () => CurveValidator.Validate(FactoryCpu, [8, 3, 3]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Up_thresholds_must_strictly_increase()
    {
        var curve = With(FactoryCpu, 3, new FanPoint(64, 70));

        ValidateCpu(curve).Should().Contain(e => e.Contains("artan"));
    }

    [Fact]
    public void Speeds_must_not_decrease()
    {
        var curve = With(FactoryCpu, 3, new FanPoint(70, 55));

        ValidateCpu(curve).Should().Contain(e => e.Contains("hız"));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(96)]
    public void Up_thresholds_stay_in_range(int up)
    {
        var index = up < 50 ? 1 : 6;
        var curve = With(FactoryCpu, index, new FanPoint(up, FactoryCpu.Points[index].SpeedPercent));

        ValidateCpu(curve).Should().NotBeEmpty();
    }

    [Fact]
    public void Speed_cannot_exceed_one_hundred_percent()
    {
        var curve = With(FactoryCpu, 6, new FanPoint(88, 101));

        ValidateCpu(curve).Should().Contain(e => e.Contains("100"));
    }

    [Theory]
    [InlineData(58)] // 58 - 3 = 55: equal to the previous up threshold, no hysteresis left
    [InlineData(57)] // 57 - 3 = 54: below it
    public void Down_threshold_from_the_factory_offset_must_stay_above_the_previous_up_threshold(int up)
    {
        var curve = With(FactoryCpu, 2, new FanPoint(up, 60));

        ValidateCpu(curve).Should().Contain(e => e.Contains("aşağı"));
    }

    [Fact]
    public void Down_threshold_one_degree_above_the_previous_up_threshold_is_valid()
    {
        var curve = With(FactoryCpu, 2, new FanPoint(59, 60));

        ValidateCpu(curve).Should().BeEmpty();
    }

    [Fact]
    public void Last_step_must_start_at_or_below_ninety_degrees()
    {
        var curve = With(FactoryCpu, 6, new FanPoint(91, 80));

        ValidateCpu(curve).Should().Contain(e => e.Contains("90"));
    }

    [Fact]
    public void Last_step_must_run_at_least_eighty_percent()
    {
        var curve = With(FactoryCpu, 6, new FanPoint(88, 79));

        ValidateCpu(curve).Should().Contain(e => e.Contains("80"));
    }

    [Fact]
    public void First_point_carries_no_threshold()
    {
        var curve = With(FactoryCpu, 0, new FanPoint(40, 45));

        ValidateCpu(curve).Should().NotBeEmpty();
    }

    [Fact]
    public void A_fan_that_stays_off_until_high_temperatures_is_rejected()
    {
        var curve = FanCurve.Of((0, 0), (84, 0), (85, 0), (86, 0), (87, 0), (88, 0), (90, 80));

        ValidateCpu(curve).Should().NotBeEmpty();
    }

    [Fact]
    public void The_first_step_must_start_by_sixty_five_degrees()
    {
        var curve = FanCurve.Of((0, 40), (66, 50), (70, 60), (74, 70), (78, 75), (82, 80), (88, 80));

        ValidateCpu(curve).Should().Contain(e => e.Contains("65"));
    }

    [Fact]
    public void The_fan_must_reach_half_speed_by_seventy_five_degrees()
    {
        var curve = FanCurve.Of((0, 20), (60, 30), (68, 40), (75, 45), (80, 70), (85, 80), (90, 100));

        ValidateCpu(curve).Should().Contain(e => e.Contains("75"));
    }

    [Fact]
    public void Down_thresholds_are_up_thresholds_minus_the_offsets()
    {
        FactoryCpu.DownThresholdsC(CpuOffsets).Should().Equal(47, 61, 67, 73, 79, 85);
        FactoryDefaults.FanCurves.Gpu.DownThresholdsC(FactoryDefaults.GpuDownOffsets).Should().Equal(47, 58, 62, 68, 74, 81);
    }
}
