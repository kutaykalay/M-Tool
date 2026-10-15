using System.Windows;
using System.Windows.Input;
using MTool.App.ViewModels;
using MTool.App.Views.Chart;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Tests.Views;

public sealed class CurveChartScaleTests
{
    // Offset from the origin so a scale that forgets Left/Top is caught.
    private static readonly Rect Plot = new(40, 10, 160, 200);

    private static readonly PointLimits AnyLimits = new(40, 90, 20, 100);

    public static TheoryData<double, double> PlotSizes => new()
    {
        { 530, 220 },
        { 397.6, 176.8 }, // fractional, as at 125 % and 150 %
        { 81, 101 }, // one pixel per unit
    };

    [Fact]
    public void The_axes_start_and_end_on_the_plot_corners()
    {
        CurveChartScale.ToPixel(CurveChartScale.MinC, 0, Plot).Should().Be(new Point(40, 210));
        CurveChartScale.ToPixel(CurveChartScale.MaxC, 100, Plot).Should().Be(new Point(200, 10));
    }

    [Theory]
    [MemberData(nameof(PlotSizes))]
    public void Every_whole_value_survives_the_round_trip(double width, double height)
    {
        var plot = new Rect(12.5, 7.25, width, height);

        for (var c = CurveChartScale.MinC; c <= CurveChartScale.MaxC; c++)
        {
            for (var pct = 0; pct <= 100; pct++)
            {
                CurveChartScale.ToValue(CurveChartScale.ToPixel(c, pct, plot), plot).Should().Be((c, pct));
            }
        }
    }

    [Fact]
    public void A_pixel_halfway_between_two_values_rounds_up()
    {
        // 2 px per °C and 2 px per %: x = 41 is 20.5 °C, y = 109 is 50.5 %.
        CurveChartScale.ToValue(new Point(41, 109), Plot).Should().Be((21, 51));
        CurveChartScale.ToValue(new Point(40.99, 109.01), Plot).Should().Be((20, 50));
    }

    [Theory]
    [InlineData(0, 110, 20, 50)] // left
    [InlineData(500, 110, 100, 50)] // right
    [InlineData(120, -50, 60, 100)] // above
    [InlineData(120, 900, 60, 0)] // below
    [InlineData(-10, -10, 20, 100)] // top left corner
    public void A_pixel_outside_the_plot_lands_on_its_edge(double x, double y, int c, int pct) =>
        CurveChartScale.ToValue(new Point(x, y), Plot).Should().Be((c, pct));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(0, 0)]
    public void An_empty_plot_has_no_value(double width, double height) =>
        CurveChartScale.ToValue(new Point(0, 0), new Rect(0, 0, width, height)).Should().BeNull();

    [Fact]
    public void Rect_empty_has_no_value() =>
        CurveChartScale.ToValue(new Point(0, 0), Rect.Empty).Should().BeNull();

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.NaN)]
    public void A_pixel_that_is_not_a_number_has_no_value(double x, double y) =>
        CurveChartScale.ToValue(new Point(x, y), Plot).Should().BeNull();

    [Fact]
    public void An_infinitely_far_pixel_lands_on_the_edge() =>
        CurveChartScale.ToValue(new Point(double.PositiveInfinity, double.NegativeInfinity), Plot).Should().Be((100, 100));

    [Fact]
    public void A_plot_of_infinite_size_has_no_value() =>
        CurveChartScale.ToValue(new Point(0, 0), new Rect(0, 0, double.PositiveInfinity, 100)).Should().BeNull();

    [Fact]
    public void The_ticks_cannot_be_changed_by_a_caller() =>
        CurveChartScale.XTicks.Should().NotBeAssignableTo<int[]>();

    [Fact]
    public void The_axis_covers_every_writable_threshold_and_where_it_drops_back()
    {
        var largestOffset = FactoryDefaults.CpuDownOffsets.Concat(FactoryDefaults.GpuDownOffsets).Max();

        CurveChartScale.MinC.Should().BeLessThanOrEqualTo(EcWriteRules.MinUpThresholdC - largestOffset);
        CurveChartScale.MaxC.Should().BeGreaterThanOrEqualTo(EcWriteRules.MaxUpThresholdC);
    }

    [Fact]
    public void The_ticks_span_both_axes()
    {
        CurveChartScale.XTicks.Should().Equal(20, 30, 40, 50, 60, 70, 80, 90, 100);
        CurveChartScale.YTicks.Should().Equal(0, 20, 40, 60, 80, 100);
    }

    [Fact]
    public void The_idle_point_sits_on_the_left_edge_whatever_its_threshold()
    {
        CurveChartScale.PointPosition(new PointViewModel(0, 0, 45, AnyLimits)).Should().Be((20.0, 45.0));
        CurveChartScale.PointPosition(new PointViewModel(0, 70, 45, AnyLimits)).Should().Be((20.0, 45.0));
    }

    [Fact]
    public void A_step_point_sits_on_its_threshold()
    {
        CurveChartScale.PointPosition(new PointViewModel(3, 70, 70, AnyLimits)).Should().Be((70.0, 70.0));
    }

    [Fact]
    public void The_curve_is_drawn_as_steps_from_edge_to_edge()
    {
        var vertices = CurveChartScale.StepVertices(FactoryDefaults.FanCurves.Cpu);

        vertices.Should().Equal(
            (20.0, 45.0), (55.0, 45.0), (55.0, 50.0), (64.0, 50.0), (64.0, 60.0), (70.0, 60.0), (70.0, 70.0),
            (76.0, 70.0), (76.0, 75.0), (82.0, 75.0), (82.0, 80.0), (88.0, 80.0), (88.0, 80.0), (100.0, 80.0));
    }

    [Fact]
    public void An_invalid_curve_is_drawn_inside_the_axes_without_throwing()
    {
        var curve = FanCurve.Of((0, -5), (120, 50), (10, 60), (70, 70), (76, 75), (82, 180), (88, 80));

        var vertices = CurveChartScale.StepVertices(curve);

        vertices.Should().HaveCount(14);
        vertices.Should().OnlyContain(v => v.C >= 20 && v.C <= 100 && v.Pct >= 0 && v.Pct <= 100);
        vertices[0].Should().Be((20.0, 0.0));
        vertices[1].Should().Be((100.0, 0.0));
        vertices[3].Should().Be((20.0, 50.0));
        vertices[10].Should().Be((82.0, 100.0));
    }

    [Fact]
    public void A_single_point_is_a_flat_line()
    {
        CurveChartScale.StepVertices(FanCurve.Of((0, 40))).Should().Equal((20.0, 40.0), (100.0, 40.0));
    }

    [Fact]
    public void An_empty_curve_draws_nothing()
    {
        CurveChartScale.StepVertices(new FanCurve([])).Should().BeEmpty();
    }

    [Fact]
    public void Each_step_drops_back_at_its_down_threshold()
    {
        var cpu = FactoryDefaults.FanCurves.Cpu;
        var gpu = FactoryDefaults.FanCurves.Gpu;

        var cpuMarkers = CurveChartScale.DownMarkers(cpu, cpu.DownThresholdsC(FactoryDefaults.CpuDownOffsets));
        var gpuMarkers = CurveChartScale.DownMarkers(gpu, gpu.DownThresholdsC(FactoryDefaults.GpuDownOffsets));

        cpuMarkers.Select(m => m.C).Should().Equal(47, 61, 67, 73, 79, 85);
        gpuMarkers.Select(m => m.C).Should().Equal(47, 58, 62, 68, 74, 81);
        cpuMarkers[0].Should().Be((47.0, 50.0, 45.0));
        cpuMarkers[5].Should().Be((85.0, 80.0, 80.0));
    }

    [Fact]
    public void The_lowest_possible_drop_back_stays_on_the_axis()
    {
        var curve = FanCurve.Of((0, 30), (30, 40), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80));

        var markers = CurveChartScale.DownMarkers(curve, curve.DownThresholdsC(FactoryDefaults.CpuDownOffsets));

        markers[0].C.Should().Be(22);
    }

    [Fact]
    public void A_drop_back_off_the_axis_is_clipped_to_it()
    {
        var curve = FanCurve.Of((0, 30), (40, 40), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80));

        var markers = CurveChartScale.DownMarkers(curve, [10, 3, 3, 3, 3, 3]);

        markers[0].C.Should().Be(20);
    }

    [Fact]
    public void A_down_marker_keeps_its_speeds_on_the_axis()
    {
        var curve = FanCurve.Of((0, -5), (55, 120), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80));

        var markers = CurveChartScale.DownMarkers(curve, [47, 61, 67, 73, 79, 85]);

        markers[0].Should().Be((47.0, 100.0, 0.0));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void Down_markers_need_one_threshold_per_step(int count)
    {
        var act = () => CurveChartScale.DownMarkers(FactoryDefaults.FanCurves.Cpu, [.. Enumerable.Repeat(50, count)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_empty_curve_has_no_down_markers()
    {
        CurveChartScale.DownMarkers(new FanCurve([]), []).Should().BeEmpty();
    }

    [Fact]
    public void A_step_point_may_move_inside_a_box()
    {
        var point = new PointViewModel(2, 62, 60, new PointLimits(59, 66, 50, 70));

        CurveChartScale.LimitsArea(point).Should().Be((59.0, 66.0, 50.0, 70.0));
    }

    [Fact]
    public void The_idle_point_may_only_move_up_and_down_the_left_edge()
    {
        var idle = new PointViewModel(0, 0, 30, new PointLimits(0, 0, 0, 50));

        CurveChartScale.LimitsArea(idle).Should().Be((20.0, 20.0, 0.0, 50.0));
    }

    [Theory]
    [InlineData(70, 60, 50, 70)]
    [InlineData(40, 90, 70, 50)]
    public void An_empty_range_has_no_area(int minUp, int maxUp, int minSpeed, int maxSpeed) =>
        CurveChartScale.LimitsArea(new PointViewModel(3, 65, 60, new PointLimits(minUp, maxUp, minSpeed, maxSpeed)))
            .Should().BeNull();

    [Fact]
    public void The_idle_point_ignores_its_threshold_range()
    {
        var idle = new PointViewModel(0, 0, 30, new PointLimits(5, 0, 0, 50));

        CurveChartScale.LimitsArea(idle).Should().Be((20.0, 20.0, 0.0, 50.0));
    }

    [Theory]
    [InlineData(Key.Left, false, 69, 60)]
    [InlineData(Key.Right, false, 71, 60)]
    [InlineData(Key.Up, false, 70, 61)]
    [InlineData(Key.Down, false, 70, 59)]
    [InlineData(Key.Left, true, 65, 60)]
    [InlineData(Key.Right, true, 75, 60)]
    [InlineData(Key.Up, true, 70, 65)]
    [InlineData(Key.Down, true, 70, 55)]
    public void Arrow_keys_step_by_one_or_by_five_with_shift(Key key, bool shift, int upC, int pct) =>
        CurveChartScale.KeyStep(new PointViewModel(3, 70, 60, AnyLimits), key, shift).Should().Be((upC, pct));

    [Theory]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    public void Left_and_right_leave_the_idle_point_where_it_is(Key key) =>
        CurveChartScale.KeyStep(new PointViewModel(0, 0, 45, AnyLimits), key, shift: false).Should().Be((0, 45));

    [Theory]
    [InlineData(Key.Up, 50)]
    [InlineData(Key.Down, 40)]
    public void Up_and_down_move_the_idle_point_speed_only(Key key, int pct) =>
        CurveChartScale.KeyStep(new PointViewModel(0, 0, 45, AnyLimits), key, shift: true).Should().Be((0, pct));

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Tab)]
    [InlineData(Key.Escape)]
    public void Other_keys_do_not_move_a_point(Key key) =>
        CurveChartScale.KeyStep(new PointViewModel(3, 70, 60, AnyLimits), key, shift: false).Should().BeNull();

    [Fact]
    public void A_step_past_the_limit_is_still_asked_for_and_left_to_the_editor()
    {
        var atTop = new PointViewModel(6, 90, 100, new PointLimits(83, 90, 80, 100));

        CurveChartScale.KeyStep(atTop, Key.Up, shift: false).Should().Be((90, 101));
    }
}
