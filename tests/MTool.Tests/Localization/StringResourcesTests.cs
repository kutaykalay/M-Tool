using System.Globalization;
using MTool.App.Resources;
using MTool.Core.Localization;
using MTool.Core.Resources;

namespace MTool.Tests.Localization;

public sealed class StringResourcesTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    [Fact]
    public void Tests_run_in_Turkish_so_existing_text_assertions_keep_their_meaning()
    {
        CultureInfo.CurrentUICulture.Name.Should().Be("tr-TR");
        CultureInfo.CurrentCulture.Name.Should().Be("tr-TR");
    }

    [Fact]
    public void Supported_languages_are_English_and_Turkish_with_English_first()
    {
        SupportedLanguages.All.Should().Equal("en", "tr");
        SupportedLanguages.Neutral.Should().Be("en");
    }

    [Fact]
    public void Supported_language_names_are_canonical_culture_names()
    {
        foreach (var name in SupportedLanguages.All)
        {
            CultureInfo.GetCultureInfo(name).Name.Should().Be(name);
        }
    }

    [Fact]
    public void App_strings_follow_the_ui_culture()
    {
        Strings.Main_SectionSensors.Should().Be("SENSÖRLER");
        Strings.ResourceManager.GetString(nameof(Strings.Main_SectionSensors), English).Should().Be("SENSORS");
        Strings.ResourceManager.GetString(nameof(Strings.Main_SectionSensors), Turkish).Should().Be("SENSÖRLER");
    }

    [Fact]
    public void Core_strings_follow_the_ui_culture()
    {
        CoreStrings.Settings_UnknownLanguage.Should().Be("Bilinmeyen dil ({0}); Windows dili kullanılıyor.");
        CoreStrings.ResourceManager.GetString(nameof(CoreStrings.Settings_UnknownLanguage), English)
            .Should().Be("Unknown language ({0}); using the Windows language.");
    }

    [Fact]
    public void An_unsupported_language_falls_back_to_English()
    {
        var austrianGerman = CultureInfo.GetCultureInfo("de-AT");

        Strings.ResourceManager.GetString(nameof(Strings.Main_SectionSensors), austrianGerman).Should().Be("SENSORS");
    }
}
