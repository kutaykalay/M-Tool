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
        var curves = new P65Device(P65Memory.Faz0Snapshot()).ReadFanCurves();

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

    private static FanCurves CopyOf(FanCurves curves) =>
        new(new FanCurve(curves.Cpu.Points.ToList()), new FanCurve(curves.Gpu.Points.ToList()));
}
