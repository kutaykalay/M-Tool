using System.Globalization;
using MTool.Core.Localization;

namespace MTool.Tests.Localization;

public sealed class UiLanguageTests
{
    private static readonly string[] EnTr = ["en", "tr"];
    private static readonly string[] Wide = ["en", "tr", "de", "pt-BR", "zh-Hans"];

    private static string Resolve(string? setting, string windows, params string[] supported) =>
        UiLanguage.Resolve(setting, CultureInfo.GetCultureInfo(windows), supported).Name;

    [Fact]
    public void A_regional_Windows_language_falls_back_to_its_neutral_one_when_supported()
    {
        Resolve(null, "de-AT", Wide).Should().Be("de");
    }

    [Fact]
    public void A_Windows_language_that_is_not_supported_gives_English()
    {
        Resolve(null, "de-AT", EnTr).Should().Be("en");
    }

    [Fact]
    public void Turkish_Windows_gives_Turkish()
    {
        Resolve(null, "tr-TR", EnTr).Should().Be("tr");
    }

    [Fact]
    public void Simplified_Chinese_regions_give_zh_Hans_when_supported()
    {
        Resolve(null, "zh-CN", Wide).Should().Be("zh-Hans");
    }

    [Fact]
    public void Traditional_Chinese_gives_English_without_zh_Hant()
    {
        Resolve(null, "zh-TW", Wide).Should().Be("en");
    }

    [Fact]
    public void Portuguese_from_Portugal_gives_English_but_Brazilian_keeps_its_own()
    {
        Resolve(null, "pt-PT", Wide).Should().Be("en");
        Resolve(null, "pt-BR", Wide).Should().Be("pt-BR");
    }

    [Fact]
    public void A_chosen_language_wins_over_Windows()
    {
        Resolve("tr", "en-US", EnTr).Should().Be("tr");
    }

    [Theory]
    [InlineData("TR")]
    [InlineData("Tr")]
    public void A_chosen_language_is_matched_without_regard_to_case(string setting)
    {
        Resolve(setting, "en-US", EnTr).Should().Be("tr");
    }

    [Fact]
    public void The_canonical_spelling_of_the_supported_list_is_returned()
    {
        Resolve("PT-br", "en-US", Wide).Should().Be("pt-BR");
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../tr")]
    public void A_language_that_is_not_supported_follows_Windows(string setting)
    {
        Resolve(setting, "tr-TR", EnTr).Should().Be("tr");
        Resolve(setting, "fr-FR", EnTr).Should().Be("en");
    }

    [Fact]
    public void The_invariant_Windows_language_gives_English()
    {
        UiLanguage.Resolve(null, CultureInfo.InvariantCulture, EnTr).Name.Should().Be("en");
    }

    [Fact]
    public void The_result_does_not_depend_on_the_current_culture()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Resolve(null, "tr-TR", EnTr).Should().Be("tr");
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void Canonical_name_finds_a_supported_language_or_nothing()
    {
        UiLanguage.CanonicalName("TR", EnTr).Should().Be("tr");
        UiLanguage.CanonicalName("xx", EnTr).Should().BeNull();
        UiLanguage.CanonicalName(null, EnTr).Should().BeNull();
    }

    [Fact]
    public void Every_shipped_language_is_a_culture_Windows_knows()
    {
        foreach (var language in SupportedLanguages.All)
        {
            UiLanguage.Resolve(language, CultureInfo.InvariantCulture, SupportedLanguages.All).Name.Should().Be(language);
        }
    }
}
