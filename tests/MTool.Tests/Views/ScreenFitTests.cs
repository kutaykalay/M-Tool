using System.Windows;
using MTool.App.Views;

namespace MTool.Tests.Views;

public sealed class ScreenFitTests
{
    private const double Margin = 12;
    private const double Precision = 1e-9;

    // A 1920×1080 screen with a 48 px taskbar at the bottom; at 150 % it is 1280 × 688.
    private static readonly Rect FullHd = new(0, 0, 1920, 1032);
    private static readonly DpiScale At150 = new(1.5, 1.5);

    [Fact]
    public void A_window_that_fits_is_centred_in_the_work_area()
    {
        var place = ScreenFit.Centered(FullHd, At150, new Size(440, 500), Margin);

        place.Left.Should().BeApproximately((1280 - 440) / 2.0, Precision);
        place.Top.Should().BeApproximately((688 - 500) / 2.0, Precision);
        place.MaxHeight.Should().BeApproximately(688 - (2 * Margin), Precision);
    }

    [Fact]
    public void A_window_taller_than_the_screen_is_capped_and_keeps_its_title_bar_on_screen()
    {
        // At 175 % the screen is 590 high: the window with the live values is taller than that.
        var place = ScreenFit.Centered(FullHd, new DpiScale(1.75, 1.75), new Size(440, 900), Margin);

        place.Top.Should().BeApproximately(Margin, Precision);
        place.MaxHeight.Should().BeApproximately((1032 / 1.75) - (2 * Margin), Precision);
    }

    [Fact]
    public void A_window_inside_the_work_area_stays_where_it_is()
    {
        var place = ScreenFit.KeepInside(FullHd, At150, new Rect(100, 80, 440, 500), Margin);

        (place.Left, place.Top).Should().Be((100d, 80d));
    }

    [Fact]
    public void A_window_that_grew_past_the_bottom_moves_up_only_as_far_as_needed()
    {
        var place = ScreenFit.KeepInside(FullHd, At150, new Rect(100, 300, 440, 500), Margin);

        place.Left.Should().Be(100);
        place.Top.Should().BeApproximately(688 - 500 - Margin, Precision);
    }

    [Fact]
    public void A_window_off_the_top_left_comes_back_with_its_title_bar_first()
    {
        var place = ScreenFit.KeepInside(FullHd, At150, new Rect(-50, -40, 440, 2000), Margin);

        (place.Left, place.Top).Should().Be((Margin, Margin));
    }

    [Fact]
    public void A_work_area_that_does_not_start_at_the_origin_is_respected()
    {
        var area = new Rect(100, 50, 1820, 1030);

        var place = ScreenFit.Centered(area, new DpiScale(1, 1), new Size(440, 2000), Margin);

        place.Left.Should().BeApproximately(100 + ((1820 - 440) / 2.0), Precision);
        place.Top.Should().BeApproximately(50 + Margin, Precision);
        place.MaxHeight.Should().BeApproximately(1030 - (2 * Margin), Precision);
    }
}
