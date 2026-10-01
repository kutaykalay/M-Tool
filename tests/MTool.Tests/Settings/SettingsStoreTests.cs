using MTool.Core.Device;
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
}
