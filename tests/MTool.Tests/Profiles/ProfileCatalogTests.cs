using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Profiles;

public class ProfileCatalogTests
{
    private static readonly ProfileCatalog Catalog = ProfileCatalog.BuiltIn;

    [Fact]
    public void Built_in_profiles_are_default_cool_and_silent_in_that_order()
    {
        Catalog.Profiles.Select(p => p.Name).Should().Equal("Default", "Cool", "Silent");
    }

    [Fact]
    public void Curves_with_equal_points_are_equal_even_when_they_are_different_lists()
    {
        var copy = new FanCurve(FactoryDefaults.FanCurves.Cpu.Points.ToList());

        copy.Should().Be(FactoryDefaults.FanCurves.Cpu);
        copy.GetHashCode().Should().Be(FactoryDefaults.FanCurves.Cpu.GetHashCode());
    }

    [Fact]
    public void Factory_tables_read_from_the_ec_match_default()
    {
        var curves = new P65Device(P65Memory.FactorySnapshot()).ReadFanCurves();

        Catalog.Match(curves)!.Name.Should().Be("Default");
    }

    [Theory]
    [InlineData("Cool")]
    [InlineData("Silent")]
    public void Preset_tables_match_their_profile(string name)
    {
        var copied = CopyOf(Catalog.Find(name)!.Curves);

        Catalog.Match(copied)!.Name.Should().Be(name);
    }

    [Fact]
    public void A_table_one_byte_off_matches_nothing()
    {
        var cpu = FactoryDefaults.FanCurves.Cpu.Points.ToArray();
        cpu[3] = cpu[3] with { SpeedPercent = cpu[3].SpeedPercent + 1 };
        var curves = FactoryDefaults.FanCurves with { Cpu = new FanCurve(cpu) };

        Catalog.Match(curves).Should().BeNull();
    }

    [Theory]
    [InlineData("Cool", "Cool")]
    [InlineData("cool", "Cool")]
    [InlineData("DEFAULT", "Default")]
    public void Find_ignores_case(string query, string expected)
    {
        Catalog.Find(query)!.Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Turbo")]
    [InlineData("")]
    public void Unknown_name_is_not_found(string name)
    {
        Catalog.Find(name).Should().BeNull();
    }

    [Fact]
    public void Custom_profiles_come_after_the_built_in_ones_in_their_own_order()
    {
        var catalog = Catalog.WithCustom([Custom("Gece"), Custom("Oyun")]);

        catalog.Profiles.Select(p => p.Name).Should().Equal("Default", "Cool", "Silent", "Gece", "Oyun");
    }

    [Fact]
    public void With_custom_replaces_the_previous_custom_profiles()
    {
        var catalog = Catalog.WithCustom([Custom("Gece")]).WithCustom([Custom("Oyun")]);

        catalog.Profiles.Select(p => p.Name).Should().Equal("Default", "Cool", "Silent", "Oyun");
    }

    [Fact]
    public void A_custom_profile_with_a_built_in_name_never_becomes_built_in()
    {
        var catalog = Catalog.WithCustom([Custom("cool")]).WithCustom([]);

        catalog.Profiles.Select(p => p.Name).Should().Equal("Default", "Cool", "Silent");
    }

    [Fact]
    public void A_preferred_built_in_profile_wins_over_an_equal_custom_one()
    {
        var catalog = Catalog.WithCustom([FactoryDefaults.Profile with { Name = "Fabrika" }]);

        catalog.Match(CopyOf(FactoryDefaults.FanCurves), "Default")!.Name.Should().Be("Default");
        catalog.Match(CopyOf(FactoryDefaults.FanCurves), "Fabrika")!.Name.Should().Be("Fabrika");
    }

    [Fact]
    public void With_custom_leaves_the_built_in_catalog_alone()
    {
        var catalog = ProfileCatalog.BuiltIn.WithCustom([Custom("Gece")]);

        catalog.Should().NotBeSameAs(ProfileCatalog.BuiltIn);
        ProfileCatalog.BuiltIn.Profiles.Select(p => p.Name).Should().Equal("Default", "Cool", "Silent");
    }

    [Theory]
    [InlineData("Default", true)]
    [InlineData("cool", true)]
    [InlineData("SILENT", true)]
    [InlineData("Gece", false)]
    [InlineData("", false)]
    public void Is_built_in_ignores_case(string name, bool expected)
    {
        ProfileCatalog.IsBuiltIn(name).Should().Be(expected);
    }

    [Fact]
    public void Equal_tables_match_the_preferred_profile()
    {
        var catalog = Catalog.WithCustom([Presets.Cool with { Name = "Cool kopya" }]);

        catalog.Match(CopyOf(Presets.Cool.Curves), "Cool kopya")!.Name.Should().Be("Cool kopya");
        catalog.Match(CopyOf(Presets.Cool.Curves), "COOL KOPYA")!.Name.Should().Be("Cool kopya");
    }

    [Fact]
    public void Without_a_preference_equal_tables_match_the_first_profile()
    {
        var catalog = Catalog.WithCustom([Presets.Cool with { Name = "Cool kopya" }]);

        catalog.Match(CopyOf(Presets.Cool.Curves), preferredName: null)!.Name.Should().Be("Cool");
    }

    [Fact]
    public void A_preferred_profile_with_other_tables_does_not_win()
    {
        var catalog = Catalog.WithCustom([Custom("Gece")]);

        catalog.Match(CopyOf(Presets.Cool.Curves), "Gece")!.Name.Should().Be("Cool");
        catalog.Match(CopyOf(Presets.Cool.Curves), "Turbo")!.Name.Should().Be("Cool");
    }

    private static FanProfile Custom(string name) => new(name, Presets.Silent.Curves with
    {
        Cpu = FanCurve.Of((0, 30), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)),
    });

    private static FanCurves CopyOf(FanCurves curves) =>
        new(new FanCurve(curves.Cpu.Points.ToList()), new FanCurve(curves.Gpu.Points.ToList()));
}
