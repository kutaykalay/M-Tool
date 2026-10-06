using MTool.Core.Device;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Settings;

namespace MTool.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void Missing_file_yields_safe_defaults()
    {
        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Settings.DryRun.Should().BeTrue();
        result.Settings.SchemaVersion.Should().Be(1);
        result.Warning.Should().BeNull();
    }

    [Fact]
    public void Saved_settings_round_trip()
    {
        var store = new SettingsStore(_folder);
        var settings = AppSettings.Default with
        {
            DryRun = false,
            Desired = new DesiredState("Cool", PerformanceMode.Balanced, 60, FanMode.Auto),
        };

        store.Save(settings);

        store.Load().Settings.Should().Be(settings);
        File.ReadAllText(SettingsPath).Should().Contain("\"schemaVersion\": 1");
    }

    [Fact]
    public void Corrupt_file_is_set_aside_and_defaults_are_used()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Warning.Should().Contain("settings.json");
        File.Exists(SettingsPath).Should().BeFalse();
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle();
    }

    [Fact]
    public void A_corrupt_file_that_cannot_be_moved_aside_still_loads_defaults()
    {
        File.WriteAllText(SettingsPath, "{ not json");
        // Another process (antivirus, an editor) holds the file: reading works, moving it does not.
        using var held = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Warning.Should().Contain("settings.json").And.Contain("varsayılanlar kullanılıyor");
        File.Exists(SettingsPath).Should().BeTrue();
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().BeEmpty();
    }

    [Fact]
    public void A_file_that_was_unreadable_at_load_is_never_saved_over()
    {
        var store = new SettingsStore(_folder);
        var custom = AppSettings.Default with { CustomProfiles = [Custom("Gece")] };
        store.Save(custom);
        SettingsLoadResult loaded;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            loaded = store.Load();
        }

        var save = () => store.Save(loaded.Settings);

        loaded.Warning.Should().Contain("okunamadı");
        save.Should().Throw<IOException>().WithMessage("*yeniden başlatın*");
        new SettingsStore(_folder).Load().Settings.Should().Be(custom);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 1, "dryRun": false, "futureThing": 3 }""");

        new SettingsStore(_folder).Load().Settings.DryRun.Should().BeFalse();
    }

    [Fact]
    public void Save_does_not_leave_temporary_files()
    {
        var store = new SettingsStore(_folder);

        store.Save(AppSettings.Default);
        store.Save(AppSettings.Default with { Desired = new DesiredState("Silent") });

        Directory.GetFiles(_folder).Select(Path.GetFileName).Should().Equal("settings.json");
    }

    [Fact]
    public void Enums_are_written_as_names()
    {
        var store = new SettingsStore(_folder);

        store.Save(AppSettings.Default with { Desired = new DesiredState("Cool", PerformanceMode.Eco, FanMode: FanMode.Auto) });

        var json = File.ReadAllText(SettingsPath);
        json.Should().Contain("\"performance\": \"Eco\"").And.Contain("\"fanMode\": \"Auto\"");
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "dryRun": false }""")]
    [InlineData("""{ "schemaVersion": 1, "dryRun": false, "desired": null }""")]
    public void Missing_desired_state_falls_back_to_default(string json)
    {
        File.WriteAllText(SettingsPath, json);

        new SettingsStore(_folder).Load().Settings.Desired.Should().Be(DesiredState.Default);
    }

    [Theory]
    [InlineData("""{ "desired": { "fanProfile": "Cool", "chargeLimitPercent": "x" } }""")]
    [InlineData("""{ "desired": { "fanProfile": "Cool", "performance": 7 } }""")]
    [InlineData("""{ "desired": { "fanProfile": "Cool", "performance": "Turbo" } }""")]
    public void Unusable_desired_state_sets_the_file_aside(string json)
    {
        File.WriteAllText(SettingsPath, json);

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Warning.Should().NotBeNull();
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle();
    }

    [Fact]
    public void Missing_file_wants_default_fan_table_balanced_and_full_charge()
    {
        var desired = new SettingsStore(_folder).Load().Settings.Desired;

        desired.Should().Be(new DesiredState("Default", PerformanceMode.Balanced, ChargeLimitPercent: 100));
    }

    [Fact]
    public void Saved_choices_win_over_first_run_defaults()
    {
        File.WriteAllText(
            SettingsPath,
            """{ "desired": { "fanProfile": "Silent", "performance": null, "chargeLimitPercent": 60 } }""");

        var desired = new SettingsStore(_folder).Load().Settings.Desired;

        desired.Should().Be(new DesiredState("Silent", Performance: null, ChargeLimitPercent: 60));
    }

    [Fact]
    public void Desired_state_cannot_be_set_to_null()
    {
        (AppSettings.Default with { Desired = null! }).Desired.Should().Be(DesiredState.Default);
    }

    private static FanProfile Custom(string name) => new(name, Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    });

    [Fact]
    public void Custom_profiles_round_trip_with_turkish_names()
    {
        var store = new SettingsStore(_folder);
        var settings = AppSettings.Default with
        {
            Desired = new DesiredState("Işıklı gece"),
            CustomProfiles = [Custom("Işıklı gece"), Custom("Öğle İşi ğüş")],
        };

        store.Save(settings);

        store.Load().Settings.Should().Be(settings);
        File.ReadAllText(SettingsPath).Should().Contain("\"customProfiles\"").And.Contain("\"schemaVersion\": 1");
    }

    [Fact]
    public void Settings_with_equal_custom_profiles_in_different_lists_are_equal()
    {
        var first = AppSettings.Default with { CustomProfiles = [Custom("Gece")] };
        var second = AppSettings.Default with { CustomProfiles = new List<FanProfile> { Custom("Gece") } };

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(AppSettings.Default);
    }

    [Fact]
    public void The_order_of_custom_profiles_matters_for_equality()
    {
        var ab = AppSettings.Default with { CustomProfiles = [Custom("A"), Custom("B")] };
        var ba = AppSettings.Default with { CustomProfiles = [Custom("B"), Custom("A")] };

        ab.Should().NotBe(ba);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "dryRun": false }""")]
    [InlineData("""{ "schemaVersion": 1, "dryRun": false, "customProfiles": null }""")]
    public void Missing_custom_profiles_load_as_an_empty_list(string json)
    {
        File.WriteAllText(SettingsPath, json);

        new SettingsStore(_folder).Load().Settings.CustomProfiles.Should().BeEmpty();
    }

    [Fact]
    public void Custom_profiles_cannot_be_set_to_null()
    {
        (AppSettings.Default with { CustomProfiles = null! }).CustomProfiles.Should().BeEmpty();
    }

    [Fact]
    public void Text_in_a_number_field_of_a_custom_profile_sets_the_file_aside()
    {
        File.WriteAllText(
            SettingsPath,
            """{ "customProfiles": [ { "name": "Gece", "curves": { "cpu": { "points": [ { "upThresholdC": 0, "speedPercent": "x" } ] } } } ] }""");

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle();
    }

    [Fact]
    public void A_hand_written_file_with_null_parts_and_repeated_names_loads_and_sanitizes_without_an_exception()
    {
        File.WriteAllText(SettingsPath, """
            { "desired": { "fanProfile": "Gece" },
              "customProfiles": [
                { "name": "Gece", "curves": { "cpu": { "points": null }, "gpu": { "points": [] } } },
                { "name": "gece" },
                null,
                { "name": "Gece", "curves": { "cpu": { "points": [ null ] }, "gpu": null } } ] }
            """);

        var loaded = new SettingsStore(_folder).Load();
        var act = () => SettingsSanitizer.Sanitize(loaded.Settings, ProfileCatalog.BuiltIn);

        loaded.Warning.Should().BeNull();
        var result = act.Should().NotThrow().Subject;
        result.Settings.CustomProfiles.Should().BeEmpty();
        result.DroppedProfiles.Should().Be(4);
        result.Settings.Desired.FanProfile.Should().Be("Default");
    }

    [Fact]
    public void Preserve_copy_keeps_a_copy_and_leaves_the_file_alone()
    {
        const string json = """{ "schemaVersion": 1, "customProfiles": [ { "name": "Gece" } ] }""";
        File.WriteAllText(SettingsPath, json);

        var message = new SettingsStore(_folder).PreserveCopy();

        File.ReadAllText(SettingsPath).Should().Be(json);
        var copy = Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle().Subject;
        File.ReadAllText(copy).Should().Be(json);
        message.Should().Contain(Path.GetFileName(copy));
    }

    [Fact]
    public void An_oversized_file_is_set_aside_without_being_read()
    {
        File.WriteAllText(SettingsPath, "{ \"dryRun\": false, \"pad\": \"" + new string('x', SettingsStore.MaxFileBytes) + "\" }");

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Warning.Should().Contain("büyük");
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle();
    }

    [Fact]
    public void Preserve_copy_does_not_pile_up_copies_of_an_unchanged_file()
    {
        File.WriteAllText(SettingsPath, """{ "customProfiles": [ { "name": "Gece" } ] }""");
        var store = new SettingsStore(_folder);

        var first = store.PreserveCopy();
        var second = store.PreserveCopy();

        var copy = Path.GetFileName(Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle().Subject);
        first.Should().Contain(copy);
        second.Should().Contain(copy);
    }

    [Fact]
    public void Preserve_copy_keeps_a_new_copy_when_the_file_changed()
    {
        var store = new SettingsStore(_folder);
        File.WriteAllText(SettingsPath, """{ "customProfiles": [ { "name": "Gece" } ] }""");
        store.PreserveCopy();
        File.WriteAllText(SettingsPath, """{ "customProfiles": [ { "name": "Oyun" } ] }""");

        store.PreserveCopy();

        Directory.GetFiles(_folder, "settings.json.bad-*").Should().HaveCount(2);
    }

    [Fact]
    public void Preserve_copy_of_a_locked_file_says_the_copy_was_not_kept()
    {
        File.WriteAllText(SettingsPath, "{}");
        using var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var message = new SettingsStore(_folder).PreserveCopy();

        message.Should().Contain("saklanamadı");
    }

    [Fact]
    public void Settings_are_not_equal_to_null()
    {
        AppSettings.Default.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Preserve_copy_without_a_file_does_nothing()
    {
        new SettingsStore(_folder).PreserveCopy().Should().BeNull();

        Directory.GetFiles(_folder).Should().BeEmpty();
    }

    [Fact]
    public void A_file_of_exactly_the_size_limit_is_read()
    {
        const string json = """{ "dryRun": false }""";
        File.WriteAllText(SettingsPath, json + new string(' ', SettingsStore.MaxFileBytes - json.Length));

        var result = new SettingsStore(_folder).Load();

        result.Warning.Should().BeNull();
        result.Settings.DryRun.Should().BeFalse();
    }

    [Fact]
    public void A_file_with_a_byte_order_mark_is_read()
    {
        File.WriteAllText(SettingsPath, """{ "dryRun": false, "desired": { "fanProfile": "Işıklı" } }""", new System.Text.UTF8Encoding(true));

        var result = new SettingsStore(_folder).Load();

        result.Warning.Should().BeNull();
        result.Settings.Desired.FanProfile.Should().Be("Işıklı");
    }

    [Fact]
    public void A_file_from_a_newer_version_loads_defaults_and_is_never_saved_over()
    {
        const string json = """{ "schemaVersion": 2, "dryRun": false, "customProfiles": [ { "name": "Gece" } ] }""";
        File.WriteAllText(SettingsPath, json);
        var store = new SettingsStore(_folder);

        var result = store.Load();
        var save = () => store.Save(result.Settings);

        result.Settings.Should().Be(AppSettings.Default);
        result.Warning.Should().Contain("daha yeni");
        save.Should().Throw<IOException>().WithMessage("*daha yeni*");
        File.ReadAllText(SettingsPath).Should().Be(json);
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_schema_version_below_one_sets_the_file_aside(int version)
    {
        File.WriteAllText(SettingsPath, $$"""{ "schemaVersion": {{version}}, "dryRun": false }""");

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Warning.Should().Contain("sürüm");
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle();
    }

    [Fact]
    public void Setting_a_file_aside_keeps_only_the_newest_copies()
    {
        var old = OldCopies(SettingsStore.MaxKeptCopies + 2);
        File.WriteAllText(SettingsPath, "{ not json");

        new SettingsStore(_folder).Load();

        var left = Directory.GetFiles(_folder, "settings.json.bad-*").Select(Path.GetFileName).ToArray();
        left.Should().HaveCount(SettingsStore.MaxKeptCopies);
        left.Should().NotContain(old.Take(3));
        left.Should().Contain(old.Skip(3));
    }

    [Fact]
    public void Preserve_copy_keeps_only_the_newest_copies()
    {
        var old = OldCopies(SettingsStore.MaxKeptCopies);
        File.WriteAllText(SettingsPath, """{ "customProfiles": [ { "name": "Gece" } ] }""");

        var message = new SettingsStore(_folder).PreserveCopy();

        var left = Directory.GetFiles(_folder, "settings.json.bad-*").Select(Path.GetFileName).ToArray();
        left.Should().HaveCount(SettingsStore.MaxKeptCopies);
        left.Should().NotContain(old[0]);
        message.Should().Contain("saklandı");
    }

    [Fact]
    public void An_old_copy_that_cannot_be_deleted_does_not_stop_the_new_one_being_kept()
    {
        var old = OldCopies(SettingsStore.MaxKeptCopies);
        File.WriteAllText(SettingsPath, "{ not json");
        using var held = new FileStream(Path.Combine(_folder, old[0]), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = new SettingsStore(_folder).Load();

        result.Warning.Should().Contain("olarak saklandı");
        File.Exists(SettingsPath).Should().BeFalse();
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().HaveCount(SettingsStore.MaxKeptCopies + 1);
    }

    [Fact]
    public void A_file_holding_only_null_is_set_aside()
    {
        File.WriteAllText(SettingsPath, "null");

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Warning.Should().Contain("boş ya da eksik");
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle();
    }

    [Fact]
    public void The_new_copy_is_kept_even_when_the_clock_went_back()
    {
        // Named as if made in the future: the clock was set back since, so the new copy sorts first.
        OldCopies(SettingsStore.MaxKeptCopies, year: 2099);
        const string json = "{ not json";
        File.WriteAllText(SettingsPath, json);

        var result = new SettingsStore(_folder).Load();

        var copies = Directory.GetFiles(_folder, "settings.json.bad-*");
        copies.Should().HaveCount(SettingsStore.MaxKeptCopies);
        copies.Select(File.ReadAllText).Should().Contain(json);
        result.Warning.Should().Contain("olarak saklandı");
    }

    [Fact]
    public void A_corrupt_file_that_could_not_be_moved_aside_is_never_saved_over()
    {
        const string json = "{ not json";
        File.WriteAllText(SettingsPath, json);
        var store = new SettingsStore(_folder);
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Load();
        }

        var save = () => store.Save(AppSettings.Default);

        save.Should().Throw<IOException>().WithMessage("*yeniden başlatın*");
        File.ReadAllText(SettingsPath).Should().Be(json);
    }

    [Fact]
    public void Loading_again_after_the_file_became_readable_allows_saving()
    {
        var store = new SettingsStore(_folder);
        store.Save(AppSettings.Default);
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Load();
        }

        store.Load();
        var save = () => store.Save(AppSettings.Default with { DryRun = false });

        save.Should().NotThrow();
        store.Load().Settings.DryRun.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "dryRun": false }""")]
    [InlineData("""{ "schemaVersion": 1, "dryRun": false, "powerSwitch": null }""")]
    public void Missing_power_switch_loads_as_off_without_pairs(string json)
    {
        File.WriteAllText(SettingsPath, json);

        var powerSwitch = new SettingsStore(_folder).Load().Settings.PowerSwitch;

        powerSwitch.Should().Be(PowerSwitchSettings.Default);
        powerSwitch.Enabled.Should().BeFalse();
        powerSwitch.Ac.Should().BeNull();
        powerSwitch.Battery.Should().BeNull();
    }

    [Fact]
    public void Power_switch_round_trips_with_turkish_names_and_untouched_performance()
    {
        var store = new SettingsStore(_folder);
        var settings = AppSettings.Default with
        {
            PowerSwitch = new PowerSwitchSettings(
                Enabled: true,
                Ac: new PowerProfilePair("Cool", PerformanceMode.High),
                Battery: new PowerProfilePair("Işıklı gece", Performance: null)),
        };

        store.Save(settings);

        store.Load().Settings.Should().Be(settings);
        File.ReadAllText(SettingsPath).Should().Contain("\"powerSwitch\"").And.Contain("\"battery\"").And.Contain("\"High\"");
    }

    [Fact]
    public void Settings_with_different_power_switches_are_not_equal()
    {
        var on = AppSettings.Default with { PowerSwitch = new PowerSwitchSettings(Enabled: true) };

        on.Should().NotBe(AppSettings.Default);
        on.Should().Be(AppSettings.Default with { PowerSwitch = new PowerSwitchSettings(Enabled: true) });
    }

    [Fact]
    public void Power_switch_cannot_be_set_to_null()
    {
        (AppSettings.Default with { PowerSwitch = null! }).PowerSwitch.Should().Be(PowerSwitchSettings.Default);
    }

    [Fact]
    public void An_unknown_performance_name_in_a_power_pair_sets_the_file_aside()
    {
        File.WriteAllText(SettingsPath, """{ "powerSwitch": { "enabled": true, "ac": { "fanProfile": "Cool", "performance": "Turbo" } } }""");

        var result = new SettingsStore(_folder).Load();

        result.Settings.Should().Be(AppSettings.Default);
        Directory.GetFiles(_folder, "settings.json.bad-*").Should().ContainSingle();
    }

    /// <summary>Copies named like the store names them, from long ago; oldest first.</summary>
    private string[] OldCopies(int count, int year = 2020)
    {
        var names = Enumerable.Range(0, count).Select(i => $"settings.json.bad-{year}0101-0000{i:00}-old").ToArray();
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(_folder, name), $"eski {name}");
        }

        return names;
    }
}
