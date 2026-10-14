using System.Text;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

public sealed class PrivateTextTests
{
    [Theory]
    [InlineData("Model: Kutay special", "Model: <hidden> special")]
    [InlineData("Model: KUTAY special", "Model: <hidden> special")]
    [InlineData(@"C:\Users\Kutay\AppData", @"C:\Users\<hidden>\AppData")]
    [InlineData("Kutay, Kutay.", "<hidden>, <hidden>.")]
    public void Masks_a_private_word_wherever_it_stands_alone(string text, string expected) =>
        PrivateText.Mask(text, ["Kutay"]).Should().Be(expected);

    [Theory]
    [InlineData(@"C:\Users\PC2\AppData")]
    [InlineData("PCIe bus")]
    [InlineData("MSI-PC")]
    [InlineData("PC-name")]
    public void Leaves_words_that_only_contain_the_private_word(string text) =>
        PrivateText.Mask(text, ["PC"]).Should().Be(text);

    [Fact]
    public void Masks_the_longer_word_first_so_a_shorter_one_does_not_split_it() =>
        PrivateText.Mask("host KUTAY-PC here", ["KUTAY", "KUTAY-PC"]).Should().Be("host <hidden> here");

    [Theory]
    [InlineData("C++", "uses C++ daily", "uses <hidden> daily")]
    [InlineData("a.b", "a.b and axb", "<hidden> and axb")]
    public void Treats_regex_characters_in_a_word_as_plain_text(string word, string text, string expected) =>
        PrivateText.Mask(text, [word]).Should().Be(expected);

    [Fact]
    public void No_private_words_change_nothing()
    {
        PrivateText.Mask("Kutay P65", []).Should().Be("Kutay P65");
        PrivateText.HoldsPrivateName("Kutay P65", []).Should().BeFalse();
    }

    [Fact]
    public void A_private_word_that_cannot_be_normalized_is_compared_as_written()
    {
        PrivateText.HoldsPrivateName("P65 Creator", ["Ze\uD800ki"]).Should().BeFalse();
        PrivateText.HoldsPrivateName("P65 Creator", ["Creator\uD800"]).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("K")]
    public void Ignores_words_too_short_to_mask_safely(string word) =>
        PrivateText.Mask("K is a letter", [word]).Should().Be("K is a letter");

    [Theory]
    [InlineData("Kutay P65", "Kutay")]
    [InlineData("ｋｕｔａｙ P65", "Kutay")]
    [InlineData("P65 by kutay-pc", "KUTAY-PC")]
    [InlineData("Kutay P65", "  Kutay  ")]
    public void Finds_a_private_name_also_in_a_look_alike_form(string value, string word) =>
        PrivateText.HoldsPrivateName(value, [word]).Should().BeTrue();

    [Theory]
    [InlineData("P65 Creator 9SE", "Kutay")]
    [InlineData("P65 Creator 9SE", "K")]
    [InlineData("P65 Creator 9SE", "")]
    public void A_value_without_a_private_name_is_not_flagged(string value, string word) =>
        PrivateText.HoldsPrivateName(value, [word]).Should().BeFalse();

    [Fact]
    public void A_value_with_a_lone_surrogate_cannot_be_compared_and_counts_as_private() =>
        PrivateText.HoldsPrivateName("P65\uD800 Creator", ["ZEKI-PC"]).Should().BeTrue();

    [Theory]
    [InlineData(0x202E)] // right-to-left override
    [InlineData(0x200B)] // zero-width space
    [InlineData(0xE000)] // private use
    [InlineData(0x2028)] // line separator
    [InlineData(0x2029)] // paragraph separator
    [InlineData(0x0000)]
    [InlineData(0x007F)]
    [InlineData(0x001B)]
    public void Characters_that_are_not_shown_or_change_the_display_are_unprintable(int codePoint) =>
        PrivateText.IsUnprintable(new Rune(codePoint)).Should().BeTrue();

    [Theory]
    [InlineData('a')]
    [InlineData('ı')]
    [InlineData('中')]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\n')]
    public void Letters_and_white_space_are_printable(char c) =>
        PrivateText.IsUnprintable(new Rune(c)).Should().BeFalse();
}
