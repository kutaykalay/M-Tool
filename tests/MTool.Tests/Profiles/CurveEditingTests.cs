using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Tests.Profiles;

public class CurveEditingTests
{
    private static FanCurve Cpu => FactoryDefaults.FanCurves.Cpu;

    private static FanCurve Gpu => FactoryDefaults.FanCurves.Gpu;

    private static IReadOnlyList<int> CpuOffsets => FactoryDefaults.CpuDownOffsets;

    private static IReadOnlyList<int> GpuOffsets => FactoryDefaults.GpuDownOffsets;

    // Default CPU: (0,45) (55,50) (64,60) (70,70) (76,75) (82,80) (88,80); down offsets 8,3,3,3,3,3.
    [Theory]
    [InlineData(0, 0, 0, 0, 50)]
    [InlineData(1, 30, 60, 45, 60)]
    [InlineData(2, 59, 66, 50, 70)]
    [InlineData(3, 68, 72, 60, 75)]
    [InlineData(4, 74, 78, 70, 80)]
    [InlineData(5, 80, 84, 75, 80)]
    [InlineData(6, 86, 90, 80, 100)]
    public void Cpu_limits_follow_the_neighbours_and_the_down_offsets(int index, int minUp, int maxUp, int minSpeed, int maxSpeed)
    {
        CurveEditing.Limits(Cpu, index, CpuOffsets).Should().Be(new PointLimits(minUp, maxUp, minSpeed, maxSpeed));
    }

    // Default GPU: (0,0) (55,50) (61,60) (65,70) (71,80) (77,90) (86,90); down offsets 8,3,3,3,3,5.
    [Theory]
    [InlineData(0, 0, 0, 0, 50)]
    [InlineData(1, 30, 57, 0, 60)]
    [InlineData(2, 59, 61, 50, 70)]
    [InlineData(3, 65, 67, 60, 80)]
    [InlineData(4, 69, 73, 70, 90)]
    [InlineData(5, 75, 80, 80, 90)]
    [InlineData(6, 83, 90, 90, 100)]
    public void Gpu_limits_use_the_larger_last_down_offset(int index, int minUp, int maxUp, int minSpeed, int maxSpeed)
    {
        CurveEditing.Limits(Gpu, index, GpuOffsets).Should().Be(new PointLimits(minUp, maxUp, minSpeed, maxSpeed));
    }

    [Fact]
    public void The_first_step_cannot_start_above_the_envelope_temperature()
    {
        var curve = FanCurve.Of((0, 45), (55, 50), (80, 60), (84, 70), (88, 75), (92, 80), (95, 80));

        CurveEditing.Limits(curve, 1, CpuOffsets).MaxUpC.Should().Be(CurveValidator.MaxFirstStepThresholdC);
    }

    [Fact]
    public void A_middle_step_stays_inside_the_ec_threshold_range()
    {
        var curve = FanCurve.Of((0, 45), (10, 50), (50, 60), (60, 70), (70, 75), (80, 80), (99, 100));
        var small = new[] { 1, 1, 1, 1, 1, 0 };

        CurveEditing.Limits(curve, 5, small).MaxUpC.Should().Be(EcWriteRules.MaxUpThresholdC);
        CurveEditing.Limits(curve, 2, small).MinUpC.Should().Be(EcWriteRules.MinUpThresholdC);
    }

    [Fact]
    public void A_move_inside_the_limits_lands_exactly()
    {
        var moved = CurveEditing.Move(Cpu, 3, 71, 72, CpuOffsets);

        moved.Points[3].Should().Be(new FanPoint(71, 72));
    }

    [Theory]
    [InlineData(3, 0, 0, 68, 60)]
    [InlineData(3, 200, 200, 72, 75)]
    [InlineData(6, 99, 10, 90, 80)]
    [InlineData(1, -5, -5, 30, 45)]
    public void A_move_outside_the_limits_snaps_to_the_nearest_one(int index, int up, int speed, int expectedUp, int expectedSpeed)
    {
        var moved = CurveEditing.Move(Cpu, index, up, speed, CpuOffsets);

        moved.Points[index].Should().Be(new FanPoint(expectedUp, expectedSpeed));
    }

    [Fact]
    public void The_idle_point_keeps_a_zero_threshold()
    {
        var moved = CurveEditing.Move(Cpu, 0, 40, 30, CpuOffsets);

        moved.Points[0].Should().Be(new FanPoint(0, 30));
    }

    [Fact]
    public void A_move_changes_only_the_moved_point()
    {
        var moved = CurveEditing.Move(Cpu, 3, 71, 72, CpuOffsets);

        moved.Points.Where((_, i) => i != 3).Should().Equal(Cpu.Points.Where((_, i) => i != 3));
    }

    [Fact]
    public void A_move_returns_a_new_curve_and_leaves_the_input_alone()
    {
        var before = Cpu.Points.ToArray();

        var moved = CurveEditing.Move(Cpu, 3, 71, 72, CpuOffsets);

        moved.Should().NotBeSameAs(Cpu);
        Cpu.Points.Should().Equal(before);
    }

    [Fact]
    public void An_empty_range_keeps_the_point_where_it_is()
    {
        // Steps 1 and 3 are too close for the two 3 degree offsets, so step 2 has no valid threshold.
        var broken = FanCurve.Of((0, 45), (55, 50), (64, 60), (62, 70), (76, 75), (82, 80), (88, 80));

        var limits = CurveEditing.Limits(broken, 2, CpuOffsets);
        var moved = CurveEditing.Move(broken, 2, 70, 65, CpuOffsets);

        limits.MinUpC.Should().BeGreaterThan(limits.MaxUpC);
        moved.Points[2].Should().Be(new FanPoint(64, 65));
    }

    [Fact]
    public void An_empty_speed_range_keeps_the_speed()
    {
        var broken = FanCurve.Of((0, 45), (55, 70), (64, 60), (70, 65), (76, 75), (82, 80), (88, 80));

        var moved = CurveEditing.Move(broken, 2, 60, 99, CpuOffsets);

        moved.Points[2].Should().Be(new FanPoint(60, 60));
    }

    [Fact]
    public void Speed_limits_stay_inside_the_ec_range_when_a_neighbour_is_out_of_it()
    {
        var broken = FanCurve.Of((0, -5), (55, 50), (64, 60), (70, 70), (76, 75), (82, 150), (88, 80));

        CurveEditing.Limits(broken, 1, CpuOffsets).MinSpeedPercent.Should().Be(0);
        CurveEditing.Limits(broken, 4, CpuOffsets).MaxSpeedPercent.Should().Be(EcWriteRules.MaxSpeedPercent);
    }

    [Fact]
    public void The_last_point_keeps_its_speed_when_the_step_below_is_above_the_ec_maximum()
    {
        var broken = FanCurve.Of((0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 150), (88, 80));

        var moved = CurveEditing.Move(broken, 6, 88, 100, CpuOffsets);

        moved.Points[6].SpeedPercent.Should().Be(80);
    }

    [Fact]
    public void The_idle_point_of_an_invalid_curve_gets_a_zero_threshold()
    {
        var broken = FanCurve.Of((5, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80));

        CurveEditing.Move(broken, 0, 5, 45, CpuOffsets).Points[0].Should().Be(new FanPoint(0, 45));
    }

    [Fact]
    public void A_curve_that_is_not_seven_points_is_rejected()
    {
        var shortCurve = FanCurve.Of((0, 45), (55, 50));

        var act = () => CurveEditing.Limits(shortCurve, 1, CpuOffsets);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Offsets_that_do_not_match_the_curve_are_rejected()
    {
        var act = () => CurveEditing.Limits(Cpu, 1, [8, 3]);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void An_index_outside_the_curve_is_rejected(int index)
    {
        var act = () => CurveEditing.Move(Cpu, index, 50, 50, CpuOffsets);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static readonly int[] SmallOffsets = [1, 1, 1, 1, 1, 0];

    /// <summary>Valid curves to drag: the built-in ones plus edge cases the presets do not reach.</summary>
    private static readonly Dictionary<string, (FanCurve Curve, IReadOnlyList<int> Offsets)> ValidCurves = new()
    {
        ["Default CPU"] = (FactoryDefaults.FanCurves.Cpu, FactoryDefaults.CpuDownOffsets),
        ["Default GPU"] = (FactoryDefaults.FanCurves.Gpu, FactoryDefaults.GpuDownOffsets),
        ["Cool CPU"] = (Presets.Cool.Curves.Cpu, FactoryDefaults.CpuDownOffsets),
        ["Cool GPU"] = (Presets.Cool.Curves.Gpu, FactoryDefaults.GpuDownOffsets),
        ["Silent CPU"] = (Presets.Silent.Curves.Cpu, FactoryDefaults.CpuDownOffsets),
        ["Silent GPU"] = (Presets.Silent.Curves.Gpu, FactoryDefaults.GpuDownOffsets),
        ["Low, small offsets"] = (FanCurve.Of((0, 0), (30, 50), (32, 60), (34, 70), (36, 80), (38, 90), (40, 100)), SmallOffsets),
        ["High"] = (FanCurve.Of((0, 50), (60, 55), (68, 60), (75, 70), (80, 80), (85, 90), (90, 100)), FactoryDefaults.CpuDownOffsets),
        ["On the envelope"] = (EnvelopeEdge, FactoryDefaults.CpuDownOffsets),
    };

    /// <summary>
    /// Exactly 50 % at 75 °C (raising step 3 past 75 °C leaves 45 % there) and exactly the 80 % safety
    /// floor on the last step, above a slower step 5, so only the floor stops the last step dropping.
    /// </summary>
    private static FanCurve EnvelopeEdge => FanCurve.Of((0, 30), (60, 40), (68, 45), (75, 50), (80, 60), (85, 70), (90, 80));

    public static TheoryData<string> CurveNames() => [.. ValidCurves.Keys];

    private static bool BreaksEnvelopeSpeed(FanCurve curve) =>
        curve.Points.Last(p => p.UpThresholdC <= CurveValidator.EnvelopeTemperatureC).SpeedPercent < CurveValidator.EnvelopeMinSpeedPercent;

    private static IEnumerable<string> EnvelopeSpeedErrors(IEnumerable<string> errors) =>
        errors.Where(e => e.StartsWith($"Güvenlik zarfı: {CurveValidator.EnvelopeTemperatureC} °C", StringComparison.Ordinal));

    [Fact]
    public void A_move_can_break_the_envelope_speed_and_the_validator_reports_it_once()
    {
        var moved = CurveEditing.Move(EnvelopeEdge, 3, 76, 50, CpuOffsets);

        BreaksEnvelopeSpeed(moved).Should().BeTrue();
        CurveValidator.Validate(moved, CpuOffsets).Should().ContainSingle()
            .Which.Should().StartWith($"Güvenlik zarfı: {CurveValidator.EnvelopeTemperatureC} °C");
    }

    /// <summary>
    /// The editor clamps only single-point rules. The envelope speed (at least 50 % at 75 °C) spans
    /// several points, so the editor shows it as an error instead; every other rule must hold.
    /// </summary>
    [Theory]
    [MemberData(nameof(CurveNames))]
    public void Every_move_on_a_valid_curve_passes_every_rule_but_the_envelope_speed(string name)
    {
        var (curve, offsets) = ValidCurves[name];
        CurveValidator.Validate(curve, offsets).Should().BeEmpty("the starting curve must be valid");
        var failures = new List<string>();

        for (var index = 0; index < CurveValidator.PointCount; index++)
        {
            for (var up = 0; up <= 100; up++)
            {
                for (var speed = 0; speed <= 100; speed++)
                {
                    var moved = CurveEditing.Move(curve, index, up, speed, offsets);
                    var errors = CurveValidator.Validate(moved, offsets);
                    var envelope = EnvelopeSpeedErrors(errors).Count();
                    if (errors.Count != envelope || envelope != (BreaksEnvelopeSpeed(moved) ? 1 : 0))
                    {
                        failures.Add($"index {index}, ({up}, {speed}): {string.Join(" | ", errors)}");
                    }
                }
            }
        }

        failures.Should().BeEmpty();
    }
}
