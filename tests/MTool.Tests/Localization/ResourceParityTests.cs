using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions.Execution;
using MTool.App.Resources;
using MTool.Core.Localization;
using MTool.Core.Resources;
using MTool.Tests.Fakes;

namespace MTool.Tests.Localization;

/// <summary>
/// Every language has the same keys and placeholders as English, so a missing translation is a test
/// failure rather than a silent fall back to English (or a crash in string.Format).
/// </summary>
/// <remarks>
/// The satellites are read from the test output: this proves they are built, not that the published
/// single-file exe carries them. The publish smoke test (step 11) checks that by running the exe in Turkish.
/// </remarks>
public sealed partial class ResourceParityTests
{
    /// <summary>Phrases a translation must keep word for word: commands, file names and names the user types or looks for.</summary>
    private static readonly string[] ProtectedPhrases =
    [
        "M-Tool.exe --unlock --confirm", "M-Tool.exe --report --wmi2", "settings.json", "\"dryRun\": false",
        "write-lock.txt", "winget install namazso.PawnIO", "PawnIO", "Cooler Boost", "M-Tool", "CPU", "GPU",
    ];

    /// <summary>The resx bundles: name → (manifest base name, assembly).</summary>
    private static readonly Dictionary<string, (string BaseName, Assembly Assembly)> Bundles = new()
    {
        ["Strings"] = ("MTool.App.Resources.Strings", typeof(Strings).Assembly),
        ["CoreStrings"] = ("MTool.Core.Resources.CoreStrings", typeof(CoreStrings).Assembly),
    };

    // A value is tried with each of these per placeholder until one combination formats: {0:T} needs a time, {0:N0} a number.
    private static readonly object[] SampleArguments = [42, new DateTime(2026, 10, 7, 23, 30, 0), TimeSpan.FromMinutes(90), "text"];

    public static TheoryData<string, string> Translations()
    {
        var data = new TheoryData<string, string>();
        foreach (var bundle in Bundles.Keys)
        {
            foreach (var language in SupportedLanguages.All.Where(l => l != SupportedLanguages.Neutral))
            {
                data.Add(bundle, language);
            }
        }

        return data;
    }

    public static TheoryData<string, string> AllLanguages()
    {
        var data = new TheoryData<string, string>();
        foreach (var bundle in Bundles.Keys)
        {
            foreach (var language in SupportedLanguages.All)
            {
                data.Add(bundle, language);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_has_exactly_the_English_keys(string bundle, string language)
    {
        var english = Neutral(bundle);
        var translated = Satellite(bundle, language);

        using var _ = new AssertionScope();
        translated.Keys.Except(english.Keys).Should().BeEmpty("a key English lacks is never read");
        english.Keys.Except(translated.Keys).Should().BeEmpty("a missing key falls back to English unnoticed");
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_has_the_English_placeholders(string bundle, string language)
    {
        var english = Neutral(bundle);
        var translated = Satellite(bundle, language);

        using var _ = new AssertionScope();
        foreach (var (key, value) in translated.Where(pair => english.ContainsKey(pair.Key)))
        {
            Placeholders(value).Should().BeEquivalentTo(Placeholders(english[key]), $"{language} {key}");
        }
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_keeps_the_protected_phrases(string bundle, string language)
    {
        var english = Neutral(bundle);
        var translated = Satellite(bundle, language);

        using var _ = new AssertionScope();
        foreach (var (key, value) in translated.Where(pair => english.ContainsKey(pair.Key)))
        {
            foreach (var phrase in ProtectedPhrases.Where(phrase => english[key].Contains(phrase, StringComparison.Ordinal)))
            {
                value.Should().Contain(phrase, $"{language} {key} must keep \"{phrase}\" as it is");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Every_value_is_filled_and_formats(string bundle, string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var values = language == SupportedLanguages.Neutral ? Neutral(bundle) : Satellite(bundle, language);

        using var _ = new AssertionScope();
        foreach (var (key, value) in values)
        {
            value.Should().NotBeNullOrWhiteSpace($"{language} {bundle}.{key}");
            Formats(culture, value).Should().BeTrue($"{language} {bundle}.{key} must be a valid format string: \"{value}\"");
        }
    }

    [Fact]
    public void The_package_satellite_list_matches_the_supported_languages()
    {
        var project = XDocument.Load(Path.Combine(RepoFiles.Root, "src", "MTool.App", "MTool.App.csproj"));
        var listed = project.Descendants("SatelliteResourceLanguages").Single().Value
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        listed.Should().BeEquivalentTo(SupportedLanguages.All);
    }

    [Fact]
    public void Both_assemblies_declare_English_as_the_neutral_language()
    {
        foreach (var (_, assembly) in Bundles.Values)
        {
            var neutral = assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>();
            neutral.Should().NotBeNull($"{assembly.GetName().Name} needs <NeutralLanguage>");
            neutral!.CultureName.Should().Be(SupportedLanguages.Neutral);
        }
    }

    // A fresh ResourceManager each time: one that has looked up "tr" caches that set under "tr-TR" too,
    // and would then report a satellite that does not exist.
    private static Dictionary<string, string> Neutral(string bundle) => Read(bundle, CultureInfo.InvariantCulture)!;

    private static Dictionary<string, string> Satellite(string bundle, string language)
    {
        var values = Read(bundle, CultureInfo.GetCultureInfo(language));
        values.Should().NotBeNull($"the {language} satellite of {bundle} must be built into the app");
        return values!;
    }

    private static Dictionary<string, string>? Read(string bundle, CultureInfo culture)
    {
        var (baseName, assembly) = Bundles[bundle];
        var set = new ResourceManager(baseName, assembly).GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        return set?.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    // "{{" and "}}" are escaped braces, not placeholders.
    private static List<string> Placeholders(string value) =>
        [.. Placeholder().Matches(EscapedBraces().Replace(value, "")).Select(m => m.Value)];

    private static bool Formats(CultureInfo culture, string value)
    {
        var count = Placeholders(value).Select(p => int.Parse(Placeholder().Match(p).Groups[1].Value, CultureInfo.InvariantCulture) + 1)
            .DefaultIfEmpty(0).Max();
        return Combinations(count).Any(arguments => TryFormat(culture, value, arguments));
    }

    private static IEnumerable<object[]> Combinations(int count) =>
        count == 0
            ? [[]]
            : Combinations(count - 1).SelectMany(head => SampleArguments.Select(sample => (object[])[.. head, sample]));

    private static bool TryFormat(CultureInfo culture, string value, object[] arguments)
    {
        try
        {
            _ = string.Format(culture, value, arguments);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"\{(\d+)(,[^}]*)?(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{\{|\}\}")]
    private static partial Regex EscapedBraces();
}
