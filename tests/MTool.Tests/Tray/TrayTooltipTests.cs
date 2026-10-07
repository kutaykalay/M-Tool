using MTool.App.Tray;
using MTool.Core.Device;
using MTool.Core.Sensors;

namespace MTool.Tests.Tray;

public class TrayTooltipTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 22, 0, 0, TimeSpan.Zero);
    private static readonly SensorSnapshot Warm = new(60, 46, 50, 0, 3044, 0);

    [Fact]
    public void Shows_profile_temperatures_and_rpm()
    {
        var text = TrayTooltip.Format(new SensorReading(Warm, SensorStatus.Live, 0, At), "Cool", showGpu: true);

        text.Should().Be("M-Tool · Cool\nCPU 60°C 3044 rpm\nGPU 46°C 0 rpm");
    }

    [Fact]
    public void A_single_fan_model_has_no_gpu_line()
    {
        var text = TrayTooltip.Format(new SensorReading(Warm, SensorStatus.Live, 0, At), "Cool", showGpu: false);

        text.Should().Be("M-Tool · Cool\nCPU 60°C 3044 rpm");
    }

    [Fact]
    public void Missing_temperature_is_a_dash()
    {
        var reading = new SensorReading(Warm with { GpuTempC = null }, SensorStatus.Live, 0, At);

        TrayTooltip.Format(reading, "Cool", showGpu: true).Should().Contain("GPU —°C 0 rpm");
    }

    [Theory]
    [InlineData(SensorStatus.Stale, "(veri eski)")]
    [InlineData(SensorStatus.Paused, "(duraklatıldı)")]
    public void Marks_data_that_is_not_live(SensorStatus status, string marker)
    {
        var text = TrayTooltip.Format(new SensorReading(Warm, status, 3, At), "Cool", showGpu: true);

        text.Split('\n')[0].Should().Be($"M-Tool · Cool {marker}");
    }

    [Fact]
    public void Before_the_first_reading_says_so()
    {
        TrayTooltip.Format(SensorReading.Initial, "Default", showGpu: true).Should().Be("M-Tool · Default\nSensörler okunuyor…");
    }

    [Fact]
    public void Worst_case_fits_the_tooltip_limit()
    {
        var hot = new SensorSnapshot(110, 110, 100, 100, 65535, 65535);

        var text = TrayTooltip.Format(new SensorReading(hot, SensorStatus.Stale, 9, At), "Silent", showGpu: true);

        text.Length.Should().BeLessThanOrEqualTo(TrayTooltip.MaxLength);
    }

    [Fact]
    public void A_long_profile_name_is_shortened_to_fit()
    {
        var text = TrayTooltip.Format(new SensorReading(Warm, SensorStatus.Stale, 3, At), new string('x', 80), showGpu: true);

        text.Length.Should().BeLessThanOrEqualTo(TrayTooltip.MaxLength);
        text.Should().Contain("…").And.Contain("(veri eski)").And.Contain("CPU 60°C");
    }
}
