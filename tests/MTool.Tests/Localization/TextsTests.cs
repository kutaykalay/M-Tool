using System.Globalization;
using MTool.App.Resources;
using MTool.Core.Device;

namespace MTool.Tests.Localization;

public sealed class TextsTests
{
    [Fact]
    public void Mode_names_in_the_window_stay_Turkish_in_Turkish()
    {
        Texts.Mode(PerformanceMode.High).Should().Be("Yüksek");
        Texts.Mode(PerformanceMode.Balanced).Should().Be("Dengeli");
        Texts.Mode(PerformanceMode.Eco).Should().Be("Pil");
    }

    [Theory]
    [InlineData(PerformanceMode.High, "High")]
    [InlineData(PerformanceMode.Balanced, "Balanced")]
    [InlineData(PerformanceMode.Eco, "Eco")]
    public void Mode_names_in_the_window_are_English_in_English(PerformanceMode mode, string expected)
    {
        var ui = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        try
        {
            Texts.Mode(mode).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
        }
    }
}
