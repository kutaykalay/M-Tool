using System.Globalization;
using MTool.Core.Device;
using MTool.Core.Localization;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Settings;

/// <summary>
/// The settings texts in English, and the rule that the log copy of a warning is always English. The
/// rest of the settings tests run in Turkish and stay unchanged.
/// </summary>
public sealed class SettingsTextLocalizationTests : IDisposable
{
    private static readonly ProfileCatalog Catalog = ProfileCatalog.BuiltIn;

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    /// <summary>One of every warning the sanitizer can give.</summary>
    private static AppSettings Messy()
    {
        var weak = TestCurves.Night with { Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 70), (85, 70), (90, 70)) };
        return new AppSettings(
            Desired: new DesiredState("Nope", (PerformanceMode)42, 5, (FanMode)42),
            CustomProfiles:
            [
                new FanProfile("Weak", weak),
                new FanProfile(" Padded", TestCurves.Night),
                new FanProfile("Default", TestCurves.Night),
                new FanProfile("Broken", new FanCurves(new FanCurve([]), TestCurves.Night.Gpu)),
                .. Enumerable.Range(0, 12).Select(i => new FanProfile($"Extra {i}", TestCurves.Night)),
            ],
            PowerSwitch: new PowerSwitchSettings(true, new PowerProfilePair("", null), new PowerProfilePair("Gone", null)),
            Language: "xx");
    }

    [Fact]
    public void Sanitizer_warnings_come_in_the_ui_language_and_the_log_copy_is_always_English()
    {
        var settings = Messy();
        SanitizedSettings turkish = SettingsSanitizer.Sanitize(settings, Catalog);
        SanitizedSettings english;
        using (TestCulture.Use("en"))
        {
            english = SettingsSanitizer.Sanitize(settings, Catalog);
        }

        turkish.Warnings.Should().HaveCountGreaterThan(8);
        turkish.LogWarnings.Should().Equal(english.Warnings);
        english.LogWarnings.Should().Equal(english.Warnings);
        turkish.Warnings.Should().NotEqual(turkish.LogWarnings);
        turkish.LogWarnings.Should().AllSatisfy(w => w.Should().NotMatchRegex("[çğıöşüÇĞİÖŞÜ]"));
    }

    [Fact]
    public void An_unknown_language_gives_the_banner_text_and_an_English_log_text()
    {
        var settings = new AppSettings(Language: "xx");

        var turkish = SettingsSanitizer.Sanitize(settings, Catalog);

        turkish.Warnings.Should().Equal("Bilinmeyen dil (xx); Windows dili kullanılıyor.");
        turkish.LogWarnings.Should().Equal("Unknown language (xx); using the Windows language.");
        using var _ = TestCulture.Use("en");
        var english = SettingsSanitizer.Sanitize(settings, Catalog);
        english.Warnings.Should().Equal(turkish.LogWarnings);
    }

    [Fact]
    public void Sanitizer_warnings_are_in_English_in_English()
    {
        using var _ = TestCulture.Use("en");

        var result = SettingsSanitizer.Sanitize(Messy(), Catalog);

        result.Warnings.Should().Contain("Unknown fan profile (Nope); Default is used.");
        result.Warnings.Should().Contain("Invalid performance mode (42); ignored.");
        result.Warnings.Should().Contain("Invalid fan mode (42); ignored.");
        result.Warnings.Should().Contain("Invalid charge limit (5%); it must be 50-100%, ignored.");
        result.Warnings.Should().Contain(w => w.StartsWith("Custom profile \"Weak\" was set aside: it breaks the fan rules (CPU: Safety rule", StringComparison.Ordinal));
        result.Warnings.Should().Contain(w => w.Contains("\"Default\"", StringComparison.Ordinal) && w.Contains("already exists", StringComparison.Ordinal));
        result.Warnings.Should().Contain(w => w.Contains("there can be at most 10 custom profiles", StringComparison.Ordinal));
        result.Warnings.Should().Contain("The fan profile used on AC power was not found (empty); the current setting is kept when switching to AC.");
        result.Warnings.Should().Contain("The fan profile used on battery power was not found (Gone); the current setting is kept when switching to battery.");
    }

    [Fact]
    public void A_newer_file_warns_in_the_ui_language_with_an_English_log_copy()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 2 }""");

        var turkish = new SettingsStore(_folder).Load();
        SettingsLoadResult english;
        using (TestCulture.Use("en"))
        {
            english = new SettingsStore(_folder).Load();
        }

        turkish.Warning.Should().Contain("daha yeni");
        turkish.LogWarning.Should().Be(english.Warning);
        english.Warning.Should().Be("settings.json belongs to a newer version of M-Tool; defaults are used and changes are not saved. Install the newer version.");
        english.LogWarning.Should().Be(english.Warning);
    }

    [Fact]
    public void A_file_that_is_set_aside_warns_in_both_languages_and_names_the_copy()
    {
        File.WriteAllText(SettingsPath, "null");

        SettingsLoadResult english;
        using (TestCulture.Use("en"))
        {
            english = new SettingsStore(_folder).Load();
        }

        var copy = Path.GetFileName(Directory.GetFiles(_folder, "settings.json.bad-*").Single());
        english.Warning.Should().Be($"settings.json could not be read (empty or incomplete); it was kept as {copy}, defaults are used.");
        english.LogWarning.Should().Be(english.Warning);
    }

    [Fact]
    public void A_file_that_cannot_be_read_warns_in_both_languages()
    {
        File.WriteAllText(SettingsPath, "{}");
        using var held = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var turkish = new SettingsStore(_folder).Load();

        turkish.Warning.Should().StartWith("settings.json okunamadı (");
        turkish.LogWarning.Should().StartWith("settings.json could not be read (").And.EndWith("defaults are used.");
    }

    [Fact]
    public void A_file_that_is_too_big_names_its_limit_in_English()
    {
        File.WriteAllBytes(SettingsPath, new byte[SettingsStore.MaxFileBytes + 1]);
        using var _ = TestCulture.Use("en");

        new SettingsStore(_folder).Load().Warning.Should().StartWith("settings.json could not be read (larger than 1024 KB);");
    }

    [Fact]
    public void A_save_refusal_is_worded_when_it_is_given_not_when_the_file_was_loaded()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 2 }""");
        var store = new SettingsStore(_folder);
        store.Load(); // Loaded in Turkish...

        using var _ = TestCulture.Use("en"); // ...but the user reads English by the time a save is refused.
        var save = () => store.Save(AppSettings.Default);

        save.Should().Throw<IOException>().WithMessage("settings.json belongs to a newer version of M-Tool, this version does not overwrite it; install the newer version");
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("de-AT")]
    [InlineData("zh-CN")]
    [InlineData("pt-PT")]
    public void The_load_warning_is_in_the_language_the_app_will_pick_from_Windows(string windowsLanguage)
    {
        // Load runs before the app has set its language. A load warning always goes with default settings
        // (no language field), so the app then follows Windows; the two must be the same language.
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 2 }""");
        var windows = CultureInfo.GetCultureInfo(windowsLanguage);
        var chosen = UiLanguage.Resolve(null, windows, SupportedLanguages.All);

        string beforeTheLanguageIsSet;
        using (TestCulture.Use(windowsLanguage))
        {
            beforeTheLanguageIsSet = new SettingsStore(_folder).Load().Warning!;
        }

        string afterwards;
        using (TestCulture.Use(chosen.Name))
        {
            afterwards = new SettingsStore(_folder).Load().Warning!;
        }

        beforeTheLanguageIsSet.Should().Be(afterwards);
    }

    [Fact]
    public void A_kept_copy_is_reported_in_the_ui_language_and_logged_in_English()
    {
        File.WriteAllText(SettingsPath, "{}");
        var store = new SettingsStore(_folder);

        SettingsNotice? notice;
        using (TestCulture.Use("tr"))
        {
            notice = store.PreserveCopyNotice();
        }

        var copy = Path.GetFileName(Directory.GetFiles(_folder, "settings.json.bad-*").Single());
        notice!.Text.Should().Be($"settings.json dosyasının önceki hali {copy} olarak saklandı.");
        notice.Log.Should().Be($"The previous version of settings.json was kept as {copy}.");
        new SettingsStore(Path.Combine(_folder, "none")).PreserveCopyNotice().Should().BeNull();
    }

    [Fact]
    public async Task Service_rejections_and_save_warnings_follow_the_ui_language()
    {
        var blocked = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocked, "");
        var service = new ProfileService(new FakeP65Control(), Catalog, new SettingsStore(blocked), AppSettings.Default, new ListLog());
        using var _ = TestCulture.Use("en");

        var unknown = await service.SelectProfileAsync("Nope");
        var charge = await service.SetChargeLimitAsync(5);
        var saved = await service.SelectProfileAsync("Cool");
        var missing = await service.RenameProfileAsync("Nope", "Other");

        unknown.Outcome.Message.Should().Be("Unknown fan profile: Nope");
        charge.Outcome.Message.Should().Be("The charge limit must be 50-100% (got 5%).");
        saved.SaveWarning.Should().StartWith("Could not save the settings (settings.json: ").And.EndWith("); the previous settings return after a restart.");
        missing.Error.Should().Be("There is no custom profile named \"Nope\".");
    }
}
