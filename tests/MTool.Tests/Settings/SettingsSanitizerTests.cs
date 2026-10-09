using MTool.Core.Device;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;

namespace MTool.Tests.Settings;

public class SettingsSanitizerTests
{
    private static readonly ProfileCatalog Catalog = ProfileCatalog.BuiltIn;

    [Fact]
    public void Valid_settings_pass_unchanged()
    {
        var settings = AppSettings.Default with { Desired = new DesiredState("cool", PerformanceMode.High, 80, FanMode.Advanced) };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Should().Be(settings with { Desired = settings.Desired with { FanProfile = "Cool" } });
        result.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Turbo")]
    [InlineData("")]
    public void Unknown_profile_becomes_default_with_a_warning(string profile)
    {
        var settings = AppSettings.Default with { Desired = new DesiredState(profile, PerformanceMode.Eco) };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Desired.Should().Be(new DesiredState("Default", PerformanceMode.Eco));
        result.Warnings.Should().ContainSingle().Which.Should().Contain("profil");
    }

    [Fact]
    public void Null_profile_becomes_default_with_a_warning()
    {
        var settings = AppSettings.Default with { Desired = new DesiredState(FanProfile: null!) };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Desired.FanProfile.Should().Be("Default");
        result.Warnings.Should().ContainSingle();
    }

    [Theory]
    [InlineData(49)]
    [InlineData(101)]
    [InlineData(120)]
    [InlineData(-5)]
    public void Out_of_range_charge_limit_is_dropped_with_a_warning(int percent)
    {
        var settings = AppSettings.Default with { Desired = new DesiredState("Cool", ChargeLimitPercent: percent) };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Desired.Should().Be(new DesiredState("Cool"));
        result.Warnings.Should().ContainSingle().Which.Should().Contain("şarj");
    }

    [Fact]
    public void Undefined_enum_values_are_dropped()
    {
        var settings = AppSettings.Default with
        {
            Desired = new DesiredState("Cool", (PerformanceMode)7, FanMode: (FanMode)9),
        };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Desired.Should().Be(new DesiredState("Cool"));
        result.Warnings.Should().HaveCount(2);
    }

    private static readonly FanCurves NightCurves = Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    };

    private static FanProfile Night(string name = "Gece") => new(name, NightCurves);

    private static AppSettings WithProfiles(params FanProfile[] profiles) => AppSettings.Default with { CustomProfiles = profiles };

    [Fact]
    public void Valid_custom_profiles_are_kept_and_added_to_the_catalog()
    {
        var settings = WithProfiles(Night("Gece"), Night("Oyun"));

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.CustomProfiles.Select(p => p.Name).Should().Equal("Gece", "Oyun");
        result.Catalog.Profiles.Select(p => p.Name).Should().Equal("Default", "Cool", "Silent", "Gece", "Oyun");
        result.DroppedProfiles.Should().Be(0);
        result.Warnings.Should().BeEmpty();
    }

    public static TheoryData<string, FanProfile> BrokenStructures() => new()
    {
        { "null entry", null! },
        { "null name", new FanProfile(null!, NightCurves) },
        { "null curves", new FanProfile("Gece", null!) },
        { "null cpu", new FanProfile("Gece", NightCurves with { Cpu = null! }) },
        { "null gpu", new FanProfile("Gece", NightCurves with { Gpu = null! }) },
        { "null points", new FanProfile("Gece", NightCurves with { Cpu = new FanCurve(null!) }) },
        { "null point", new FanProfile("Gece", NightCurves with { Gpu = new FanCurve([.. NightCurves.Gpu.Points.Take(6), null!]) }) },
    };

    [Theory]
    [MemberData(nameof(BrokenStructures))]
    public void A_profile_with_a_missing_part_is_dropped_without_an_exception(string reason, FanProfile profile)
    {
        var result = SettingsSanitizer.Sanitize(WithProfiles(profile), Catalog);

        result.Settings.CustomProfiles.Should().BeEmpty(reason);
        result.DroppedProfiles.Should().Be(1);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("kenara ayrıldı");
    }

    [Fact]
    public void Missing_parts_are_checked_before_names_are_compared()
    {
        var broken = new FanProfile("Gece", NightCurves with { Cpu = new FanCurve(null!) });

        var act = () => SettingsSanitizer.Sanitize(WithProfiles(broken, Night("Gece"), Night("gece"), broken), Catalog);

        act.Should().NotThrow().Which.Settings.CustomProfiles.Should().ContainSingle().Which.Name.Should().Be("Gece");
    }

    [Fact]
    public void A_curve_with_six_points_is_dropped()
    {
        var profile = Night() with { Curves = NightCurves with { Cpu = new FanCurve([.. NightCurves.Cpu.Points.Take(6)]) } };

        SettingsSanitizer.Sanitize(WithProfiles(profile), Catalog).Settings.CustomProfiles.Should().BeEmpty();
    }

    [Fact]
    public void A_curve_below_the_safety_floor_is_dropped()
    {
        var cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 70), (85, 70), (90, 70));

        var result = SettingsSanitizer.Sanitize(WithProfiles(Night() with { Curves = NightCurves with { Cpu = cpu } }), Catalog);

        result.Settings.CustomProfiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("CPU").And.Contain("Güvenlik kuralı");
    }

    [Fact]
    public void A_cpu_curve_that_breaks_the_down_offset_rule_is_dropped()
    {
        // 70 - 3 = 67 is not above 68.
        var cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (70, 65), (80, 75), (85, 85), (90, 100));

        var result = SettingsSanitizer.Sanitize(WithProfiles(Night() with { Curves = NightCurves with { Cpu = cpu } }), Catalog);

        result.Settings.CustomProfiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("CPU").And.Contain("geri iner");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_desired_profile_is_shown_as_empty(string name)
    {
        var settings = AppSettings.Default with { Desired = new DesiredState(name) };

        SettingsSanitizer.Sanitize(settings, Catalog).Warnings.Should().ContainSingle().Which.Should().Contain("(boş)");
    }

    [Fact]
    public void A_gpu_curve_that_breaks_the_down_offset_rule_is_dropped()
    {
        // The GPU's last factory down offset is 5: 86 - 5 = 81 is not above 83.
        var gpu = FanCurve.Of((0, 0), (60, 40), (67, 50), (73, 60), (78, 70), (83, 85), (86, 100));

        var result = SettingsSanitizer.Sanitize(WithProfiles(Night() with { Curves = NightCurves with { Gpu = gpu } }), Catalog);

        result.Settings.CustomProfiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("GPU");
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("cool")]
    public void A_custom_profile_with_a_built_in_name_is_dropped_and_the_built_in_wins(string name)
    {
        var result = SettingsSanitizer.Sanitize(WithProfiles(Night(name)), Catalog);

        result.Settings.CustomProfiles.Should().BeEmpty();
        result.Catalog.Find(name)!.Curves.Should().NotBe(NightCurves);
    }

    [Fact]
    public void The_second_profile_with_the_same_name_is_dropped()
    {
        var second = Night("GECE") with { Curves = Presets.Cool.Curves };

        var result = SettingsSanitizer.Sanitize(WithProfiles(Night("Gece"), second), Catalog);

        result.Settings.CustomProfiles.Should().Equal(Night("Gece"));
        result.DroppedProfiles.Should().Be(1);
    }

    [Fact]
    public void Profiles_over_the_limit_are_dropped()
    {
        var profiles = Enumerable.Range(1, ProfileCatalog.MaxCustomProfiles + 2).Select(i => Night($"Profil {i}")).ToArray();

        var result = SettingsSanitizer.Sanitize(WithProfiles(profiles), Catalog);

        result.Settings.CustomProfiles.Should().Equal(profiles.Take(ProfileCatalog.MaxCustomProfiles));
        result.DroppedProfiles.Should().Be(2);
        result.Warnings.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Gece\nERROR sahte satır")]
    [InlineData("")]
    [InlineData(" Gece")]
    public void A_profile_whose_name_breaks_the_rules_is_dropped(string name)
    {
        var result = SettingsSanitizer.Sanitize(WithProfiles(Night(name)), Catalog);

        result.Settings.CustomProfiles.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().NotContain("\n");
    }

    [Fact]
    public void A_decomposed_name_is_dropped_rather_than_rewritten()
    {
        var decomposed = "C" + (char)0x0327 + "alışma";

        SettingsSanitizer.Sanitize(WithProfiles(Night(decomposed)), Catalog).Settings.CustomProfiles.Should().BeEmpty();
    }

    [Fact]
    public void Desired_state_naming_a_dropped_profile_falls_back_to_default()
    {
        var settings = WithProfiles(Night() with { Curves = null! }) with { Desired = new DesiredState("Gece") };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Desired.FanProfile.Should().Be("Default");
        result.Warnings.Should().HaveCount(2);
    }

    [Fact]
    public void Desired_state_naming_a_valid_custom_profile_is_kept_with_its_stored_case()
    {
        var settings = WithProfiles(Night("Gece")) with { Desired = new DesiredState("GECE") };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Desired.FanProfile.Should().Be("Gece");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_desired_profile_is_shown_without_its_control_characters()
    {
        var settings = AppSettings.Default with { Desired = new DesiredState("Turbo\r\nERROR sahte") };

        var warning = SettingsSanitizer.Sanitize(settings, Catalog).Warnings.Should().ContainSingle().Subject;

        warning.Should().NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public void Many_dropped_profiles_give_a_few_warnings_and_one_summary()
    {
        var profiles = Enumerable.Range(0, 5_000).Select(_ => (FanProfile)null!).ToArray();

        var result = SettingsSanitizer.Sanitize(WithProfiles(profiles), Catalog);

        result.DroppedProfiles.Should().Be(5_000);
        result.Warnings.Should().HaveCount(SettingsSanitizer.MaxProfileWarnings + 1);
        result.Warnings[^1].Should().Contain((5_000 - SettingsSanitizer.MaxProfileWarnings).ToString());
    }

    [Fact]
    public void Profiles_past_the_limit_are_dropped_without_checking_their_curves()
    {
        var valid = Enumerable.Range(1, ProfileCatalog.MaxCustomProfiles).Select(i => Night($"Profil {i}"));
        var unsafeCurve = Night("Fazla") with { Curves = NightCurves with { Cpu = FanCurve.Of((0, 0), (90, 0), (91, 0), (92, 0), (93, 0), (94, 0), (95, 0)) } };

        var result = SettingsSanitizer.Sanitize(WithProfiles([.. valid, unsafeCurve]), Catalog);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("en fazla");
    }

    [Fact]
    public void The_dropped_count_covers_every_reason()
    {
        var settings = WithProfiles(null!, Night("Default"), Night("Gece"), Night("gece"), Night(""));

        SettingsSanitizer.Sanitize(settings, Catalog).DroppedProfiles.Should().Be(4);
    }

    private static AppSettings WithPairs(PowerProfilePair? ac, PowerProfilePair? battery) =>
        AppSettings.Default with { PowerSwitch = new PowerSwitchSettings(Enabled: true, ac, battery) };

    [Fact]
    public void Valid_power_pairs_pass_with_the_catalog_spelling()
    {
        var settings = WithProfiles(Night("Gece")) with
        {
            PowerSwitch = new PowerSwitchSettings(true, new PowerProfilePair("cool", PerformanceMode.High), new PowerProfilePair("GECE")),
        };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.PowerSwitch.Should().Be(
            new PowerSwitchSettings(true, new PowerProfilePair("Cool", PerformanceMode.High), new PowerProfilePair("Gece")));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void A_battery_pair_with_an_unknown_profile_is_dropped_with_a_warning()
    {
        var result = SettingsSanitizer.Sanitize(WithPairs(new PowerProfilePair("Cool"), new PowerProfilePair("Turbo", PerformanceMode.Eco)), Catalog);

        result.Settings.PowerSwitch.Battery.Should().BeNull();
        result.Settings.PowerSwitch.Ac.Should().Be(new PowerProfilePair("Cool"));
        result.Settings.PowerSwitch.Enabled.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("Pilde").And.Contain("Turbo").And.Contain("pile geçince");
    }

    [Fact]
    public void An_ac_pair_with_an_unknown_profile_is_dropped_with_a_warning()
    {
        var result = SettingsSanitizer.Sanitize(WithPairs(new PowerProfilePair("Turbo"), battery: null), Catalog);

        result.Settings.PowerSwitch.Ac.Should().BeNull();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("Prizde").And.Contain("prize geçince");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_power_pair_without_a_profile_name_is_dropped(string? name)
    {
        var result = SettingsSanitizer.Sanitize(WithPairs(new PowerProfilePair(name!), battery: null), Catalog);

        result.Settings.PowerSwitch.Ac.Should().BeNull();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("boş");
    }

    [Fact]
    public void An_undefined_performance_in_a_power_pair_is_dropped_but_the_profile_kept()
    {
        var result = SettingsSanitizer.Sanitize(WithPairs(new PowerProfilePair("Silent", (PerformanceMode)7), battery: null), Catalog);

        result.Settings.PowerSwitch.Ac.Should().Be(new PowerProfilePair("Silent", Performance: null));
        result.Warnings.Should().ContainSingle().Which.Should().Contain("performans");
    }

    [Fact]
    public void A_power_pair_naming_a_custom_profile_that_was_set_aside_is_dropped()
    {
        var broken = new FanProfile("Bozuk", Presets.Silent.Curves with { Cpu = FanCurve.Of((0, 100)) });
        var settings = WithProfiles(broken) with { PowerSwitch = new PowerSwitchSettings(true, Battery: new PowerProfilePair("Bozuk")) };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.PowerSwitch.Battery.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Pilde"));
    }

    [Fact]
    public void A_control_character_in_a_power_pair_name_is_not_shown_raw()
    {
        var result = SettingsSanitizer.Sanitize(WithPairs(new PowerProfilePair("Kötü\u0007ad"), battery: null), Catalog);

        result.Warnings.Should().ContainSingle().Which.Should().NotContain("\u0007").And.Contain("Kötü?ad");
    }

    [Fact]
    public void Power_pairs_do_not_change_how_the_desired_state_is_checked()
    {
        var settings = WithPairs(new PowerProfilePair("Turbo"), new PowerProfilePair("Silent")) with { Desired = new DesiredState("Cool") };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.Desired.Should().Be(new DesiredState("Cool"));
    }

    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("TR", "tr")]
    [InlineData("En", "en")]
    public void A_supported_language_is_kept_in_its_canonical_spelling(string stored, string expected)
    {
        var result = SettingsSanitizer.Sanitize(AppSettings.Default with { Language = stored }, Catalog);

        result.Settings.Language.Should().Be(expected);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void No_language_stays_none_without_a_warning()
    {
        var result = SettingsSanitizer.Sanitize(AppSettings.Default, Catalog);

        result.Settings.Language.Should().BeNull();
        result.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("../tr")]
    [InlineData("..\\..\\tr")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("tr\u0007")]
    [InlineData("tr-TR")]
    public void An_unknown_language_is_dropped_with_a_warning(string stored)
    {
        var result = SettingsSanitizer.Sanitize(AppSettings.Default with { Language = stored }, Catalog);

        result.Settings.Language.Should().BeNull();
        result.Warnings.Should().ContainSingle().Which.Should().StartWith("Bilinmeyen dil");
    }

    [Fact]
    public void The_language_in_the_warning_is_printable_and_cut_short()
    {
        var long500 = new string('a', 500);
        var withControl = "x\u0007\u001b[31m\r\ny";

        var warnings = new[] { long500, withControl }
            .Select(value => SettingsSanitizer.Sanitize(AppSettings.Default with { Language = value }, Catalog).Warnings.Single())
            .ToArray();

        warnings[0].Length.Should().BeLessThan(100);
        warnings[0].Should().Contain("…").And.NotContain(long500);
        warnings[1].Should().Contain("x??[31m??y").And.NotMatchRegex(@"\p{Cc}");
    }

    [Fact]
    public void A_language_problem_does_not_touch_profiles_or_the_desired_state()
    {
        var settings = WithProfiles(Night("Gece")) with
        {
            Language = "xx",
            Desired = new DesiredState("Cool", PerformanceMode.Eco, 70, FanMode.Auto),
        };

        var result = SettingsSanitizer.Sanitize(settings, Catalog);

        result.Settings.CustomProfiles.Should().Equal(Night("Gece"));
        result.Settings.Desired.Should().Be(settings.Desired);
        result.DroppedProfiles.Should().Be(0);
        result.Warnings.Should().ContainSingle();
    }
}
