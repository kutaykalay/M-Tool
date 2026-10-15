using MTool.App.ViewModels;
using MTool.Core.Profiles;

namespace MTool.Tests.ViewModels;

public sealed class PointViewModelTests
{
    [Fact]
    public void The_allowed_range_names_the_temperature_and_the_speed_limits()
    {
        var point = new PointViewModel(1, 50, 40, new PointLimits(40, 90, 20, 100));

        point.AllowedText.Should().Be("40–90 °C, %20–100");
    }

    [Fact]
    public void A_step_point_is_read_out_with_its_number_threshold_and_speed()
    {
        var point = new PointViewModel(2, 68, 55, new PointLimits(64, 72, 45, 65), DownC: 65);

        point.AccessibleName.Should().Be("Nokta 3: 68 °C, %55");
    }

    [Fact]
    public void The_idle_point_is_read_out_without_a_threshold()
    {
        var point = new PointViewModel(0, 0, 30, new PointLimits(0, 0, 0, 45));

        point.AccessibleName.Should().Be("Nokta 1 (boşta): %30");
    }

    [Fact]
    public void The_help_text_names_the_keys_and_the_allowed_range()
    {
        var point = new PointViewModel(1, 50, 40, new PointLimits(40, 90, 20, 100));

        point.HelpText.Should().Be("Ok tuşlarıyla taşıyın, Shift ile 5'er. İzin verilen: 40–90 °C, %20–100");
    }
}
