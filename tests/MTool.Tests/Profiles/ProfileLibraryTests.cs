using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Core.Settings;

namespace MTool.Tests.Profiles;

public class ProfileLibraryTests
{
    private static readonly FanCurves NightCurves = Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    };

    private static readonly FanCurves UnsafeCurves = NightCurves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 70), (85, 70), (90, 70)),
    };

    private static FanProfile Night(string name = "Gece") => new(name, NightCurves);

    private static AppSettings With(params FanProfile[] profiles) => AppSettings.Default with { CustomProfiles = profiles };

    private static AppSettings Updated(LibraryResult result)
    {
        result.Error.Should().BeNull();
        return result.Updated!;
    }

    private static void ShouldFail(LibraryResult result, string fragment)
    {
        result.Updated.Should().BeNull();
        result.Error.Should().ContainEquivalentOf(fragment);
    }

    [Fact]
    public void Add_appends_a_profile_with_the_trimmed_name()
    {
        var settings = With(Night("Gece"));

        var updated = Updated(ProfileLibrary.Add(settings, "  Oyun ", Presets.Cool.Curves));

        updated.CustomProfiles.Should().Equal(Night("Gece"), new FanProfile("Oyun", Presets.Cool.Curves));
        updated.Desired.Should().Be(settings.Desired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cool")]
    [InlineData("GECE")]
    [InlineData("a\nb")]
    public void Add_rejects_a_bad_or_taken_name(string name)
    {
        ShouldFail(ProfileLibrary.Add(With(Night("Gece")), name, NightCurves), "Profil");
    }

    [Fact]
    public void Add_rejects_an_unsafe_curve()
    {
        ShouldFail(ProfileLibrary.Add(With(), "Oyun", UnsafeCurves), "CPU: Güvenlik tabanı");
    }

    [Fact]
    public void Add_rejects_more_than_the_limit()
    {
        var full = With([.. Enumerable.Range(1, ProfileCatalog.MaxCustomProfiles).Select(i => Night($"P{i}"))]);

        ShouldFail(ProfileLibrary.Add(full, "Oyun", NightCurves), $"en fazla {ProfileCatalog.MaxCustomProfiles}");
    }

    [Fact]
    public void Rename_keeps_the_place_and_the_curves()
    {
        var settings = With(Night("Gece"), Night("Oyun"));

        var updated = Updated(ProfileLibrary.Rename(settings, "gece", "Sessiz gece"));

        updated.CustomProfiles.Select(p => p.Name).Should().Equal("Sessiz gece", "Oyun");
        updated.CustomProfiles[0].Curves.Should().Be(NightCurves);
    }

    [Fact]
    public void Renaming_the_desired_profile_renames_it_in_the_desired_state_too()
    {
        var settings = With(Night("Gece")) with { Desired = DesiredState.Default with { FanProfile = "Gece" } };

        var updated = Updated(ProfileLibrary.Rename(settings, "Gece", "Gece 2"));

        updated.Desired.Should().Be(settings.Desired with { FanProfile = "Gece 2" });
    }

    [Fact]
    public void Renaming_another_profile_leaves_the_desired_state_alone()
    {
        var settings = With(Night("Gece"), Night("Oyun")) with { Desired = new DesiredState("Oyun") };

        Updated(ProfileLibrary.Rename(settings, "Gece", "Gece 2")).Desired.Should().Be(settings.Desired);
    }

    [Fact]
    public void Rename_allows_changing_only_the_letter_case()
    {
        Updated(ProfileLibrary.Rename(With(Night("gece")), "gece", "Gece")).CustomProfiles[0].Name.Should().Be("Gece");
    }

    [Fact]
    public void Rename_rejects_a_taken_name()
    {
        ShouldFail(ProfileLibrary.Rename(With(Night("Gece"), Night("Oyun")), "Gece", "oyun"), "zaten var");
    }

    [Fact]
    public void Rename_rejects_a_built_in_profile()
    {
        ShouldFail(ProfileLibrary.Rename(With(), "Cool", "Serin"), "hazır");
    }

    [Fact]
    public void Rename_rejects_an_unknown_profile()
    {
        ShouldFail(ProfileLibrary.Rename(With(Night()), "Turbo", "Serin"), "bulunamadı");
    }

    [Fact]
    public void Delete_removes_a_profile_that_is_not_desired()
    {
        var updated = Updated(ProfileLibrary.Delete(With(Night("Gece"), Night("Oyun")), "GECE"));

        updated.CustomProfiles.Select(p => p.Name).Should().Equal("Oyun");
    }

    [Fact]
    public void Delete_rejects_the_desired_profile()
    {
        var settings = With(Night("Gece")) with { Desired = new DesiredState("Gece") };

        ShouldFail(ProfileLibrary.Delete(settings, "gece"), "başka bir profil seçin");
    }

    [Fact]
    public void Delete_rejects_a_built_in_profile()
    {
        ShouldFail(ProfileLibrary.Delete(With(), "Silent"), "hazır");
    }

    [Fact]
    public void Delete_rejects_an_unknown_profile()
    {
        ShouldFail(ProfileLibrary.Delete(With(Night()), "Turbo"), "bulunamadı");
    }

    [Fact]
    public void Save_curves_replaces_the_curves_of_a_custom_profile()
    {
        var settings = With(Night("Gece"), Night("Oyun"));

        var updated = Updated(ProfileLibrary.SaveCurves(settings, "oyun", Presets.Cool.Curves));

        updated.CustomProfiles.Should().Equal(Night("Gece"), new FanProfile("Oyun", Presets.Cool.Curves));
    }

    [Fact]
    public void Save_curves_rejects_an_unsafe_curve()
    {
        ShouldFail(ProfileLibrary.SaveCurves(With(Night()), "Gece", UnsafeCurves), "CPU:");
    }

    [Fact]
    public void Save_curves_rejects_a_built_in_profile()
    {
        ShouldFail(ProfileLibrary.SaveCurves(With(), "Default", NightCurves), "hazır");
    }

    [Fact]
    public void Save_curves_rejects_an_unknown_profile()
    {
        ShouldFail(ProfileLibrary.SaveCurves(With(), "Turbo", NightCurves), "bulunamadı");
    }

    public static TheoryData<string, FanCurves> MissingCurves() => new()
    {
        { "null curves", null! },
        { "null cpu", NightCurves with { Cpu = null! } },
        { "null points", NightCurves with { Gpu = new FanCurve(null!) } },
        { "null point", NightCurves with { Cpu = new FanCurve([.. NightCurves.Cpu.Points.Take(6), null!]) } },
    };

    [Theory]
    [MemberData(nameof(MissingCurves))]
    public void Missing_curve_parts_are_rejected_without_an_exception(string reason, FanCurves curves)
    {
        ShouldFail(ProfileLibrary.Add(With(), "Oyun", curves), "eksik");
        ShouldFail(ProfileLibrary.SaveCurves(With(Night()), "Gece", curves), "eksik");
        reason.Should().NotBeEmpty();
    }

    [Fact]
    public void Later_changes_to_the_callers_point_list_do_not_reach_the_stored_profile()
    {
        var points = NightCurves.Cpu.Points.ToList();
        var curves = NightCurves with { Cpu = new FanCurve(points) };

        var updated = Updated(ProfileLibrary.Add(With(), "Oyun", curves));
        points[6] = new FanPoint(90, 0);

        updated.CustomProfiles[0].Curves.Should().Be(NightCurves);
    }

    [Fact]
    public void A_case_only_rename_of_the_desired_profile_keeps_the_desired_state_in_step()
    {
        var settings = With(Night("gece")) with { Desired = new DesiredState("gece") };

        var updated = Updated(ProfileLibrary.Rename(settings, "GECE", "Gece"));

        updated.Desired.FanProfile.Should().Be("Gece");
        updated.CustomProfiles[0].Name.Should().Be("Gece");
    }

    [Fact]
    public void Add_stores_a_decomposed_name_composed()
    {
        var decomposed = "C" + (char)0x0327 + "alışma";

        Updated(ProfileLibrary.Add(With(), decomposed, NightCurves)).CustomProfiles[0].Name.Should().Be("Çalışma");
    }

    [Fact]
    public void Add_accepts_the_last_free_place()
    {
        var almostFull = With([.. Enumerable.Range(1, ProfileCatalog.MaxCustomProfiles - 1).Select(i => Night($"P{i}"))]);

        Updated(ProfileLibrary.Add(almostFull, "Oyun", NightCurves)).CustomProfiles.Should().HaveCount(ProfileCatalog.MaxCustomProfiles);
    }

    [Fact]
    public void No_operation_changes_its_input()
    {
        var settings = With(Night("Gece"), Night("Oyun")) with { Desired = new DesiredState("Gece") };
        var before = settings.CustomProfiles.ToArray();

        ProfileLibrary.Add(settings, "Yeni", NightCurves);
        ProfileLibrary.Rename(settings, "Gece", "Gece 2");
        ProfileLibrary.Delete(settings, "Oyun");
        ProfileLibrary.SaveCurves(settings, "Oyun", Presets.Cool.Curves);

        settings.CustomProfiles.Should().Equal(before);
        settings.Desired.FanProfile.Should().Be("Gece");
    }

    [Fact]
    public void Every_result_passes_the_load_sanitizer_unchanged()
    {
        var settings = With(Night("Gece")) with { Desired = new DesiredState("Gece") };
        var steps = new Func<AppSettings, LibraryResult>[]
        {
            s => ProfileLibrary.Add(s, " Oyun ", Presets.Cool.Curves),
            s => ProfileLibrary.Rename(s, "Gece", "Işıklı gece"),
            s => ProfileLibrary.SaveCurves(s, "Oyun", Presets.Silent.Curves),
            s => ProfileLibrary.Delete(s, "Oyun"),
        };

        foreach (var step in steps)
        {
            settings = Updated(step(settings));
            var sanitized = SettingsSanitizer.Sanitize(settings, ProfileCatalog.BuiltIn);
            sanitized.Settings.Should().Be(settings);
            sanitized.Warnings.Should().BeEmpty();
        }
    }
}
