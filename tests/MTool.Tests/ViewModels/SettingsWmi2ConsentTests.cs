using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Core.Settings;

namespace MTool.Tests.ViewModels;

/// <summary>The WMI2 window keeps the consent in settings.json and changes nothing else there.</summary>
public sealed class SettingsWmi2ConsentTests : IDisposable
{
    private static readonly Wmi2ExperimentalConsent Consent = new("15M3EMS1.105", new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero));

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-consent-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Saving_changes_only_the_consent_and_turning_off_removes_it()
    {
        var store = new SettingsStore(_folder);
        var loaded = AppSettings.Default with { DryRun = false, Language = "tr", Desired = new DesiredState("Cool", PerformanceMode.Eco) };
        var consent = new SettingsWmi2Consent(store, loaded);

        consent.Save(Consent);

        consent.Current.Should().Be(Consent);
        store.Load().Settings.Should().Be(loaded with { ExperimentalWmi2 = Consent });

        consent.Save(null);

        consent.Current.Should().BeNull();
        store.Load().Settings.Should().Be(loaded);
    }

    [Fact]
    public void A_consent_that_could_not_be_saved_does_not_count()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), """{ "schemaVersion": 99 }""");
        var store = new SettingsStore(_folder);
        var loaded = store.Load().Settings; // a newer M-Tool's file: this session may not save over it
        var consent = new SettingsWmi2Consent(store, loaded);

        var save = () => consent.Save(Consent);

        save.Should().Throw<IOException>();
        consent.Current.Should().BeNull();
    }
}
