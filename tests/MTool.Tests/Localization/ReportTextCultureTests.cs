using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using MTool.App.Resources;

namespace MTool.Tests.Localization;

/// <summary>The tray report's wording (G2) in both languages. The generic parity tests cover keys, placeholders and formats.</summary>
public sealed partial class ReportTextCultureTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    private static readonly Dictionary<string, string> TurkishText = new()
    {
        [nameof(Strings.Report_Create)] = "Cihaz raporu oluştur",
        [nameof(Strings.Report_CreateWmi2)] = "Cihaz raporu (WMI2 dahil)",
        [nameof(Strings.Report_OpenFolder)] = "Son raporun klasörünü aç",
        [nameof(Strings.Report_IssueMenu)] = "Sorun bildir (GitHub)",
        [nameof(Strings.Report_Working)] = "Cihaz raporu hazırlanıyor. Birkaç saniye sürebilir.",
        [nameof(Strings.Report_Failed)] = "Cihaz raporu oluşturulamadı. Ayrıntılar günlük dosyasında.",
        [nameof(Strings.Report_OpenFailed)] = "Klasör ya da tarayıcı açılamadı. Ayrıntılar günlük dosyasında.",
    };

    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var name in ReportKeys())
        {
            data.Add(name);
        }

        return data;
    }

    private static string[] ReportKeys() =>
        [.. typeof(Strings).GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.PropertyType == typeof(string) && p.Name.StartsWith("Report_", StringComparison.Ordinal))
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)];

    private static string Text(string key, CultureInfo culture) => Strings.ResourceManager.GetString(key, culture)!;

    [Fact]
    public void There_are_nine_report_texts()
    {
        ReportKeys().Should().HaveCount(9);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Every_report_text_is_filled_in_both_languages_and_differs_between_them(string key)
    {
        Text(key, English).Should().NotBeNullOrWhiteSpace();
        Text(key, Turkish).Should().NotBeNullOrWhiteSpace().And.NotBe(Text(key, English));
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Both_languages_have_the_same_number_of_placeholders(string key)
    {
        Placeholders(Text(key, Turkish)).Should().BeEquivalentTo(Placeholders(Text(key, English)));
    }

    [Fact]
    public void The_saved_text_takes_the_path_and_the_issue_item_name()
    {
        foreach (var culture in new[] { English, Turkish })
        {
            var text = string.Format(culture, Text(nameof(Strings.Report_Saved), culture), @"C:\r\x.zip", Text(nameof(Strings.Report_IssueMenu), culture));

            text.Should().Contain(@"C:\r\x.zip").And.Contain(Text(nameof(Strings.Report_IssueMenu), culture)).And.Contain("zip");
            text.Length.Should().BeLessThan(255 - 60, "the balloon is cut at 255 characters and a real path is long");
        }
    }

    [Theory]
    [MemberData(nameof(TurkishKeys))]
    public void The_turkish_texts_are_the_agreed_natural_ones(string key)
    {
        Text(key, Turkish).Should().Be(TurkishText[key]);
    }

    public static TheoryData<string> TurkishKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in TurkishText.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    [Fact]
    public void The_turkish_saved_text_reads_naturally()
    {
        Text(nameof(Strings.Report_Saved), Turkish).Should()
            .Be("Rapor kaydedildi: {0} Göndermek için tepsi menüsünden \"{1}\" öğesine basın ve zip dosyasını forma sürükleyin.");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void The_wmi2_question_says_what_is_read_and_what_is_not_written(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var text = Text(nameof(Strings.Report_Wmi2Confirm), culture);

        text.Should().Contain("Get_*").And.Contain("WMI2").And.Contain("report.txt").And.Contain("hex").And.Contain("M-Tool").And.Contain("GitHub");
        text.Should().Contain("\n").And.NotContain("&#10;", "line breaks are real newlines");
    }

    [Fact]
    public void The_turkish_wmi2_question_says_read_only_nothing_written_unfilterable_and_public()
    {
        var text = Text(nameof(Strings.Report_Wmi2Confirm), Turkish);

        text.Should().Contain("yalnızca okur").And.Contain("hiçbir şey yazılmaz").And.Contain("filtreleyemez")
            .And.Contain("herkese açık").And.Contain("göz atın");
    }

    [Fact]
    public void The_english_wmi2_question_says_read_only_nothing_written_unfilterable_and_public()
    {
        var text = Text(nameof(Strings.Report_Wmi2Confirm), English);

        text.Should().Contain("only reads").And.Contain("Nothing is written").And.Contain("cannot filter")
            .And.Contain("public").And.Contain("look through report.txt");
    }

    [Fact]
    public void Failure_texts_hold_no_path_and_no_placeholder()
    {
        foreach (var key in new[] { nameof(Strings.Report_Failed), nameof(Strings.Report_OpenFailed) })
        {
            foreach (var culture in new[] { English, Turkish })
            {
                Text(key, culture).Should().NotContain("{").And.NotContain(@"\").And.NotContain("%");
            }
        }
    }

    [Fact]
    public void The_privacy_sentence_is_the_one_key_the_command_line_also_uses()
    {
        // The tray shows Strings.Cli_ReportGuidance after the saved text instead of a second copy.
        typeof(Strings).GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(p => p.Name).Where(n => n.Contains("Guidance", StringComparison.Ordinal))
            .Should().Equal(nameof(Strings.Cli_ReportGuidance));
    }

    private static List<string> Placeholders(string value) => [.. Placeholder().Matches(value).Select(m => m.Value)];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
