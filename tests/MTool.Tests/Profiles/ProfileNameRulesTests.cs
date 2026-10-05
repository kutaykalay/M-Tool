using MTool.Core.Device;
using MTool.Core.Profiles;

namespace MTool.Tests.Profiles;

public class ProfileNameRulesTests
{
    private static readonly FanProfile Night = new("Sessiz gece", FactoryDefaults.FanCurves);

    private static readonly ProfileCatalog Catalog = new([.. ProfileCatalog.BuiltIn.Profiles, Night]);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u00A0")]
    public void Empty_or_blank_names_are_rejected(string name)
    {
        ProfileNameRules.Validate(name, Catalog).Should().NotBeNull();
    }

    [Fact]
    public void A_null_name_is_rejected()
    {
        ProfileNameRules.Validate(null, Catalog).Should().NotBeNull();
    }

    [Fact]
    public void Twenty_four_characters_are_allowed_and_twenty_five_are_not()
    {
        ProfileNameRules.Validate(new string('a', ProfileNameRules.MaxLength), Catalog).Should().BeNull();
        ProfileNameRules.Validate(new string('a', ProfileNameRules.MaxLength + 1), Catalog).Should().NotBeNull();
    }

    [Fact]
    public void The_length_limit_counts_the_trimmed_name()
    {
        var name = "  " + new string('a', ProfileNameRules.MaxLength) + "  ";

        ProfileNameRules.Validate(name, Catalog).Should().BeNull();
    }

    [Theory]
    [InlineData("Gece\nERROR sahte")]
    [InlineData("Gece\tsekme")]
    [InlineData("Gece\r")]
    [InlineData("Gece\u0000")]
    [InlineData("Gece\u001B[31m")]
    [InlineData("Gece\u0085")]
    [InlineData("Gece\u202Eters")]
    [InlineData("Gece\u2028satır")]
    public void Control_and_formatting_characters_are_rejected(string name)
    {
        ProfileNameRules.Validate(name, Catalog).Should().NotBeNull();
    }

    [Theory]
    [InlineData("default")]
    [InlineData("COOL")]
    [InlineData("Silent")]
    public void Built_in_names_are_taken_in_any_case(string name)
    {
        ProfileNameRules.Validate(name, Catalog).Should().NotBeNull();
    }

    [Theory]
    [InlineData("Sessiz gece")]
    [InlineData("SESSIZ GECE")]
    [InlineData("  sessiz gece ")]
    public void Existing_custom_names_are_taken_in_any_case(string name)
    {
        ProfileNameRules.Validate(name, Catalog).Should().NotBeNull();
    }

    [Fact]
    public void Renaming_a_profile_to_a_different_case_of_its_own_name_is_allowed()
    {
        ProfileNameRules.Validate("SESSIZ GECE", Catalog, except: "Sessiz gece").Should().BeNull();
    }

    [Fact]
    public void Except_only_frees_its_own_name()
    {
        ProfileNameRules.Validate("Cool", Catalog, except: "Sessiz gece").Should().NotBeNull();
    }

    [Fact]
    public void Normalize_trims_leading_and_trailing_whitespace()
    {
        ProfileNameRules.Normalize("  Oyun  ").Should().Be("Oyun");
    }

    [Fact]
    public void A_name_that_is_new_after_trimming_is_valid()
    {
        ProfileNameRules.Validate("  Oyun  ", Catalog).Should().BeNull();
    }

    [Theory]
    [InlineData("Çalışma")]
    [InlineData("Öğle uykusu")]
    [InlineData("ŞİMŞEK ığüö")]
    public void Turkish_letters_are_valid(string name)
    {
        ProfileNameRules.Validate(name, Catalog).Should().BeNull();
    }

    [Theory]
    [InlineData(0x200B)] // zero-width space
    [InlineData(0x200D)] // zero-width joiner
    [InlineData(0xFEFF)] // byte order mark
    [InlineData(0xD800)] // high surrogate without its low half
    [InlineData(0xDC00)] // low surrogate on its own
    public void Invisible_and_unpaired_characters_are_rejected(int code)
    {
        ProfileNameRules.Validate("Gece" + (char)code + "x", Catalog).Should().NotBeNull();
    }

    [Fact]
    public void A_leading_control_character_is_rejected_rather_than_trimmed()
    {
        ProfileNameRules.Validate("\tOyun", Catalog).Should().NotBeNull();
    }

    [Fact]
    public void Non_breaking_space_padding_is_trimmed()
    {
        var nbsp = ((char)0x00A0).ToString();

        ProfileNameRules.Validate(nbsp + "Oyun" + nbsp, Catalog).Should().BeNull();
        ProfileNameRules.Normalize(nbsp + "Oyun" + nbsp).Should().Be("Oyun");
    }

    [Fact]
    public void Emoji_made_of_surrogate_pairs_are_valid()
    {
        ProfileNameRules.Validate("Oyun " + char.ConvertFromUtf32(0x1F3AE), Catalog).Should().BeNull();
    }

    [Fact]
    public void A_decomposed_letter_is_the_same_name_as_the_composed_one()
    {
        var decomposed = "C" + (char)0x0327 + "alışma";
        var catalog = new ProfileCatalog([.. Catalog.Profiles, Night with { Name = "Çalışma" }]);

        ProfileNameRules.Normalize(decomposed).Should().Be("Çalışma");
        ProfileNameRules.Validate(decomposed, catalog).Should().NotBeNull();
    }

    [Fact]
    public void Renaming_a_profile_to_its_own_name_is_allowed()
    {
        ProfileNameRules.Validate(" Sessiz gece ", Catalog, except: "Sessiz gece").Should().BeNull();
    }

    [Fact]
    public void Each_problem_has_its_own_turkish_message()
    {
        ProfileNameRules.Validate(" ", Catalog).Should().Be("Profil adı boş olamaz.");
        ProfileNameRules.Validate("a\nb", Catalog).Should().Be("Profil adında kontrol ya da geçersiz karakter olamaz.");
        ProfileNameRules.Validate(new string('a', 25), Catalog).Should().Be("Profil adı en fazla 24 karakter olabilir (25 var).");
        ProfileNameRules.Validate("cool", Catalog).Should().Be("\"cool\" adında bir profil zaten var.");
    }

    [Fact]
    public void Printable_replaces_control_format_and_unpaired_characters()
    {
        var name = "a\r\nb" + (char)0x202E + "c" + (char)0xD800 + "d";

        ProfileNameRules.Printable(name).Should().Be("a??b?c?d");
    }

    [Fact]
    public void Printable_keeps_valid_names_and_emoji()
    {
        var name = "Işık " + char.ConvertFromUtf32(0x1F3AE);

        ProfileNameRules.Printable(name).Should().Be(name);
    }

    [Fact]
    public void Printable_shortens_long_names_and_names_null()
    {
        ProfileNameRules.Printable(new string('a', 100)).Should().Be(new string('a', ProfileNameRules.MaxLength) + "…");
        ProfileNameRules.Printable(null).Should().Be("(adsız)");
    }

    [Fact]
    public void The_first_copy_is_named_kopya()
    {
        ProfileNameRules.NextCopyName("Cool", Catalog).Should().Be("Cool kopya");
    }

    [Fact]
    public void Later_copies_are_numbered()
    {
        var catalog = new ProfileCatalog([.. Catalog.Profiles, Night with { Name = "Cool kopya" }]);

        ProfileNameRules.NextCopyName("Cool", catalog).Should().Be("Cool kopya 2");
    }

    [Fact]
    public void Numbering_skips_names_that_are_taken_in_any_case()
    {
        var catalog = new ProfileCatalog(
        [
            .. Catalog.Profiles,
            Night with { Name = "cool KOPYA" },
            Night with { Name = "Cool kopya 2" },
        ]);

        ProfileNameRules.NextCopyName("Cool", catalog).Should().Be("Cool kopya 3");
    }

    [Fact]
    public void A_copy_of_a_long_name_is_shortened_to_fit_the_limit()
    {
        var source = new string('a', ProfileNameRules.MaxLength);
        var catalog = new ProfileCatalog([.. Catalog.Profiles, Night with { Name = source }]);

        var copy = ProfileNameRules.NextCopyName(source, catalog);

        copy.Should().EndWith(" kopya");
        ProfileNameRules.Validate(copy, catalog).Should().BeNull();
    }

    [Fact]
    public void A_numbered_copy_of_a_long_name_still_fits()
    {
        var source = new string('a', ProfileNameRules.MaxLength);
        var first = new string('a', ProfileNameRules.MaxLength - " kopya".Length) + " kopya";
        var catalog = new ProfileCatalog(
        [
            .. Catalog.Profiles,
            Night with { Name = source },
            Night with { Name = first },
        ]);

        var copy = ProfileNameRules.NextCopyName(source, catalog);

        copy.Should().EndWith(" kopya 2");
        ProfileNameRules.Validate(copy, catalog).Should().BeNull();
    }

    [Fact]
    public void Shortening_never_splits_a_surrogate_pair()
    {
        // " kopya" leaves 18 characters; the emoji takes characters 18 and 19, so the cut lands inside it.
        var source = new string('a', 17) + char.ConvertFromUtf32(0x1F3AE) + "bbb";
        var catalog = new ProfileCatalog([.. Catalog.Profiles, Night with { Name = source }]);

        var copy = ProfileNameRules.NextCopyName(source, catalog);

        copy.Should().Be(new string('a', 17) + " kopya");
        ProfileNameRules.Validate(copy, catalog).Should().BeNull();
    }

    [Fact]
    public void Shortening_drops_spaces_left_at_the_cut()
    {
        var source = new string('a', 16) + "    bbbb";
        var catalog = new ProfileCatalog([.. Catalog.Profiles, Night with { Name = source }]);

        ProfileNameRules.NextCopyName(source, catalog).Should().Be(new string('a', 16) + " kopya");
    }

    [Fact]
    public void Every_suggested_copy_name_is_valid()
    {
        foreach (var profile in Catalog.Profiles)
        {
            ProfileNameRules.Validate(ProfileNameRules.NextCopyName(profile.Name, Catalog), Catalog)
                .Should().BeNull(profile.Name);
        }
    }
}
