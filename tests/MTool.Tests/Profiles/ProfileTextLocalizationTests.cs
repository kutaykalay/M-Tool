using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Core.Resources;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Profiles;

/// <summary>
/// The profile texts that reach the user, in English. The rest of the profile tests run in Turkish and
/// stay unchanged, which is the proof that the Turkish texts did not move.
/// </summary>
public sealed class ProfileTextLocalizationTests
{
    private static readonly IReadOnlyList<int> CpuOffsets = FactoryDefaults.CpuDownOffsets;

    private static readonly ProfileCatalog Catalog = ProfileCatalog.BuiltIn;

    private static FanCurve With(int index, FanPoint point)
    {
        var points = FactoryDefaults.FanCurves.Cpu.Points.ToArray();
        points[index] = point;
        return new FanCurve(points);
    }

    /// <summary>Every whole-curve rule at least once; some curves break several rules at the same time.</summary>
    public static TheoryData<string> RuleGrid() =>
    [
        "six points", "threshold on the idle point", "first step too late", "speed above 100", "speed drops",
        "threshold out of range", "thresholds not rising", "no hysteresis left", "weak last step", "slow at 75 degrees",
    ];

    private static FanCurve GridCurve(string name) => name switch
    {
        "six points" => new FanCurve(FactoryDefaults.FanCurves.Cpu.Points.Take(6).ToArray()),
        "threshold on the idle point" => With(0, new FanPoint(40, 45)),
        "first step too late" => FanCurve.Of((0, 40), (66, 50), (70, 60), (74, 70), (78, 75), (82, 80), (88, 80)),
        "speed above 100" => With(6, new FanPoint(88, 101)),
        "speed drops" => With(3, new FanPoint(70, 55)),
        "threshold out of range" => With(1, new FanPoint(29, 50)),
        "thresholds not rising" => With(3, new FanPoint(64, 70)),
        "no hysteresis left" => With(2, new FanPoint(57, 60)),
        "weak last step" => With(6, new FanPoint(91, 79)),
        "slow at 75 degrees" => FanCurve.Of((0, 20), (60, 30), (68, 40), (75, 45), (80, 70), (85, 80), (90, 100)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static string Template(string key, string culture) =>
        new ResourceManager(typeof(CoreStrings)).GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException($"No resource {key} for {culture}");

    private static IReadOnlyList<string> CurveRuleKeys() =>
        [.. new ResourceManager(typeof(CoreStrings)).GetResourceSet(CultureInfo.InvariantCulture, true, false)!
            .Cast<DictionaryEntry>().Select(e => (string)e.Key).Where(k => k.StartsWith("Curve_", StringComparison.Ordinal)).Order()];

    /// <summary>The rule a message belongs to, found by matching it against the templates of one language.</summary>
    private static string RuleOf(string message, string culture)
    {
        var matches = CurveRuleKeys().Where(key =>
            Regex.IsMatch(message, "^" + Regex.Replace(Regex.Escape(Template(key, culture)), @"\\\{\d+\}", ".+?") + "$", RegexOptions.Singleline)).ToArray();
        matches.Should().ContainSingle($"'{message}' should match exactly one {culture} curve rule");
        return matches[0];
    }

    private static string[] Rules(FanCurve curve, string culture)
    {
        using var _ = TestCulture.Use(culture);
        return [.. CurveValidator.Validate(curve, CpuOffsets).Select(m => RuleOf(m, culture))];
    }

    [Theory]
    [MemberData(nameof(RuleGrid))]
    public void A_curve_breaks_the_same_rules_in_the_same_order_in_Turkish_and_English(string name)
    {
        var curve = GridCurve(name);

        var turkish = Rules(curve, "tr");
        var english = Rules(curve, "en");

        turkish.Should().NotBeEmpty();
        english.Should().Equal(turkish);
    }

    [Fact]
    public void The_grid_covers_every_curve_rule_and_the_English_text_is_English()
    {
        var keys = CurveRuleKeys();
        keys.Should().HaveCount(11);
        var seen = RuleGrid().SelectMany(row => Rules(GridCurve(row.Data), "en")).ToHashSet();
        seen.Should().BeEquivalentTo(keys);

        using var _ = TestCulture.Use("en");
        CurveValidator.Validate(GridCurve("six points"), CpuOffsets).Should().ContainSingle()
            .Which.Should().Be("The curve must have 7 points (it has 6).");
    }

    [Fact]
    public void Both_fans_keep_their_CPU_and_GPU_prefixes_in_English()
    {
        var gpu = FanCurve.Of((0, 0), (60, 40), (67, 50), (73, 60), (78, 70), (83, 85), (87, 100));
        var cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 70), (85, 70), (90, 70));
        using var _ = TestCulture.Use("en");

        var errors = CurveValidator.ValidateWithFactoryOffsets(new FanCurves(cpu, gpu));

        errors.Should().HaveCount(2);
        errors[0].Should().StartWith("CPU: Safety rule");
        errors[1].Should().StartWith("GPU: Point 7");
    }

    [Fact]
    public void The_copy_name_follows_the_language()
    {
        using var _ = TestCulture.Use("en");
        var first = ProfileNameRules.NextCopyName("Cool", Catalog);
        var withFirst = new ProfileCatalog([.. Catalog.Profiles, Presets.Cool with { Name = first }]);

        first.Should().Be("Cool copy");
        ProfileNameRules.NextCopyName("Cool", withFirst).Should().Be("Cool copy 2");
    }

    [Fact]
    public void The_copy_name_stays_Turkish_in_Turkish()
    {
        ProfileNameRules.NextCopyName("Cool", Catalog).Should().Be("Cool kopya");
    }

    [Fact]
    public void A_profile_saved_as_Cool_kopya_keeps_its_name_in_English()
    {
        var saved = new AppSettings(CustomProfiles: [new FanProfile("Cool kopya", TestCurves.Night)]);
        using var _ = TestCulture.Use("en");

        var sanitized = SettingsSanitizer.Sanitize(saved, Catalog);

        sanitized.Warnings.Should().BeEmpty();
        sanitized.Settings.CustomProfiles.Select(p => p.Name).Should().Equal("Cool kopya");
        sanitized.Catalog.Find("Cool kopya").Should().NotBeNull();
        ProfileNameRules.NextCopyName("Cool", sanitized.Catalog).Should().Be("Cool copy");
    }

    [Fact]
    public void A_name_that_is_data_is_not_translated_by_a_library_change()
    {
        var saved = new AppSettings(CustomProfiles: [new FanProfile("Cool kopya", TestCurves.Night)]);
        using var _ = TestCulture.Use("en");

        var result = ProfileLibrary.SaveCurves(saved, "cool KOPYA", TestCurves.QuieterNight);

        result.Updated!.CustomProfiles.Select(p => p.Name).Should().Equal("Cool kopya");
    }

    [Fact]
    public void Library_and_name_errors_are_in_English()
    {
        var saved = new AppSettings(CustomProfiles: [new FanProfile("Night", TestCurves.Night)]);
        using var _ = TestCulture.Use("en");

        ProfileLibrary.Add(saved, " ", TestCurves.Night).Error.Should().Be("The profile name cannot be empty.");
        ProfileLibrary.Add(saved, "night", TestCurves.Night).Error.Should().Be("A profile named \"night\" already exists.");
        ProfileLibrary.Add(saved, new string('a', 25), TestCurves.Night).Error.Should().Be("The profile name can be at most 24 characters (25 given).");
        ProfileLibrary.Add(saved, "x\nx", TestCurves.Night).Error.Should().Be("The profile name cannot contain control or invalid characters.");
        ProfileLibrary.Delete(saved, "Default").Error.Should().Be("\"Default\" is a built-in profile and cannot be changed; copy it and edit the copy.");
        ProfileLibrary.Delete(saved, "nope").Error.Should().Be("There is no custom profile named \"nope\".");
    }
}
