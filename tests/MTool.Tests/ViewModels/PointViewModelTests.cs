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
}
