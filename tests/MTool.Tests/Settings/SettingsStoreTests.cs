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
        var settings = AppSettings.Default with { DryRun = false, SelectedProfile = "Cool" };

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
        store.Save(AppSettings.Default with { SelectedProfile = "Silent" });

        Directory.GetFiles(_folder).Select(Path.GetFileName).Should().Equal("settings.json");
    }
}
