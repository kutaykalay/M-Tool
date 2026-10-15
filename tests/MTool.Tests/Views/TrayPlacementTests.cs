using System.Windows;
using MTool.App.Views;

namespace MTool.Tests.Views;

public sealed class TrayPlacementTests
{
    private const double Margin = 12;
    private const double Precision = 1e-9;

    // A 1920×1080 screen with a 48 px taskbar at the bottom.
    private static readonly Rect FullHd = new(0, 0, 1920, 1032);

    [Fact]
    public void A_window_that_fits_sits_in_the_bottom_right_corner()
    {
        var corner = TrayPlacement.Corner(FullHd, new DpiScale(1.5, 1.5), new Size(380, 500), Margin);

        corner.Left.Should().BeApproximately(1280 - 380 - Margin, Precision);
        corner.Top.Should().BeApproximately(688 - 500 - Margin, Precision);
        corner.MaxHeight.Should().BeApproximately(688 - (2 * Margin), Precision);
    }

    [Fact]
    public void The_work_area_is_measured_at_the_window_scale_not_the_scale_the_app_started_with()
    {
        // At 175 % the same screen is 1097 × 590: a corner worked out at 150 % would be off screen.
        var corner = TrayPlacement.Corner(FullHd, new DpiScale(1.75, 1.75), new Size(380, 400), Margin);

        corner.Left.Should().BeApproximately((1920 / 1.75) - 380 - Margin, Precision);
        corner.Top.Should().BeApproximately((1032 / 1.75) - 400 - Margin, Precision);
    }

    [Fact]
    public void A_window_taller_than_the_screen_keeps_its_title_bar_on_screen()
    {
        var corner = TrayPlacement.Corner(FullHd, new DpiScale(1.75, 1.75), new Size(380, 900), Margin);

        corner.Top.Should().BeApproximately(Margin, Precision);
        corner.MaxHeight.Should().BeApproximately((1032 / 1.75) - (2 * Margin), Precision);
    }

    [Fact]
    public void A_work_area_that_does_not_start_at_the_origin_is_respected()
    {
        // Taskbar on the left and a second strip on top.
        var area = new Rect(100, 50, 1820, 1030);

        var corner = TrayPlacement.Corner(area, new DpiScale(1, 1), new Size(380, 2000), Margin);

        corner.Left.Should().BeApproximately(1920 - 380 - Margin, Precision);
        corner.Top.Should().BeApproximately(50 + Margin, Precision);
    }

    [Fact]
    public void A_window_wider_than_the_screen_starts_at_the_left_edge()
    {
        var corner = TrayPlacement.Corner(new Rect(0, 0, 800, 600), new DpiScale(1, 1), new Size(1140, 300), Margin);

        corner.Left.Should().BeApproximately(Margin, Precision);
    }

    [Fact]
    public void A_work_area_smaller_than_the_margins_gives_no_negative_height()
    {
        var corner = TrayPlacement.Corner(new Rect(0, 0, 300, 10), new DpiScale(1, 1), new Size(380, 500), Margin);

        corner.MaxHeight.Should().Be(0);
    }
}
