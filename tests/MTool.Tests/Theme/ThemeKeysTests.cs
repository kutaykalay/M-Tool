using System.Xml.Linq;
using MTool.Tests.Fakes;

namespace MTool.Tests.Theme;

/// <summary>ThemeManager swaps Light.xaml and Dark.xaml at run time, so a brush defined in only one is a missing resource in the other theme.</summary>
public sealed class ThemeKeysTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Light_and_Dark_define_exactly_the_same_keys()
    {
        var light = Keys("Light.xaml");
        var dark = Keys("Dark.xaml");

        light.Should().NotBeEmpty();
        light.Except(dark).Should().BeEmpty("a key Dark lacks is a missing resource in the dark theme");
        dark.Except(light).Should().BeEmpty("a key Light lacks is a missing resource in the light theme");
    }

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void Both_themes_define_the_second_chart_colour(string theme)
    {
        Keys(theme).Should().Contain("Brush.ChartSecondary");
    }

    private static string[] Keys(string theme) =>
        [.. XDocument.Load(Path.Combine(RepoFiles.Root, "src", "MTool.App", "Theme", theme))
            .Descendants()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .OfType<string>()];
}
