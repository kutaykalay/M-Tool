using MTool.Core.Device;
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
}
