using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Sensors;

namespace MTool.Tests.ViewModels;

public class SensorsViewModelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 19, 0, 5, TimeSpan.Zero);
    private static readonly SensorSnapshot Warm = new(60, 46, 50, 0, 3044, 0);

    [Fact]
    public void Starts_empty_and_waiting()
    {
        var sensors = new SensorsViewModel();

        sensors.CpuTemperature.Should().Be("—");
        sensors.Freshness.Should().Be("Okunuyor…");
        sensors.IsStale.Should().BeFalse();
    }

    [Fact]
    public void Shows_live_values()
    {
        var sensors = new SensorsViewModel();

        sensors.Apply(new SensorReading(Warm, SensorStatus.Live, 0, At));

        sensors.CpuTemperature.Should().Be("60 °C");
        sensors.GpuTemperature.Should().Be("46 °C");
        sensors.CpuFan.Should().Be("3044 rpm");
        sensors.GpuFan.Should().Be("0 rpm");
        sensors.Freshness.Should().BeEmpty();
        sensors.IsStale.Should().BeFalse();
    }

    [Fact]
    public void A_missing_temperature_is_a_dash()
    {
        var sensors = new SensorsViewModel();

        sensors.Apply(new SensorReading(Warm with { CpuTempC = null }, SensorStatus.Live, 0, At));

        sensors.CpuTemperature.Should().Be("—");
    }

    [Fact]
    public void Stale_data_says_when_it_was_read()
    {
        var sensors = new SensorsViewModel();

        sensors.Apply(new SensorReading(Warm, SensorStatus.Stale, 3, At));

        sensors.IsStale.Should().BeTrue();
        sensors.Freshness.Should().Be($"Veri eski ({At.ToLocalTime():HH:mm:ss})");
    }

    [Fact]
    public void Paused_polling_says_so()
    {
        var sensors = new SensorsViewModel();

        sensors.Apply(new SensorReading(Warm, SensorStatus.Paused, 0, At));

        sensors.IsStale.Should().BeTrue();
        sensors.Freshness.Should().Contain("Duraklatıldı");
    }
}
