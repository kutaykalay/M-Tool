using MTool.Core.Updates;

namespace MTool.Tests.Updates;

/// <summary>SemVer 2.0.0 parsing and precedence (Aşama 14b, K6): a release tag decides what "newer" means.</summary>
public sealed class SemVersionTests
{
    [Fact]
    public void Precedence_follows_the_chain_in_the_specification()
    {
        string[] chain =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0",
        ];

        var versions = chain.Select(v => SemVersion.Parse(v)).ToArray();

        for (var i = 1; i < versions.Length; i++)
        {
            versions[i - 1].CompareTo(versions[i]).Should().BeNegative($"{chain[i - 1]} < {chain[i]}");
            (versions[i - 1] < versions[i]).Should().BeTrue();
            (versions[i] > versions[i - 1]).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("0.15.1-beta.1", "0.16.0-beta.1")]
    [InlineData("0.16.0-beta.1", "0.16.0-beta.2")]
    [InlineData("0.16.0-beta.2", "0.16.0")]
    [InlineData("0.16.0", "0.16.1")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("1.9.9", "2.0.0")]
    [InlineData("1.0.0-9", "1.0.0-a")]
    public void The_second_is_newer(string older, string newer)
    {
        (SemVersion.Parse(older) < SemVersion.Parse(newer)).Should().BeTrue();
    }

    [Theory]
    [InlineData("1.0.0+abc", "1.0.0+def")]
    [InlineData("0.16.0-beta.1+448e0a5", "0.16.0-beta.1")]
    public void Build_metadata_does_not_count(string a, string b)
    {
        SemVersion.Parse(a).CompareTo(SemVersion.Parse(b)).Should().Be(0);
        SemVersion.Parse(a).Should().Be(SemVersion.Parse(b));
    }

    [Theory]
    [InlineData("0.16.0-beta.1+448e0a5", "0.16.0-beta.1")]
    [InlineData("1.2.3", "1.2.3")]
    public void The_text_has_no_build_metadata(string input, string text)
    {
        SemVersion.Parse(input).ToString().Should().Be(text);
    }

    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0-beta.1", true)]
    public void A_pre_release_is_told_apart(string input, bool isPrerelease)
    {
        SemVersion.Parse(input).IsPrerelease.Should().Be(isPrerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.00.0")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-beta..1")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0-beta_1")]
    [InlineData(" 1.0.0")]
    [InlineData("1.0.0 ")]
    [InlineData("1.0.0\n")]
    [InlineData("1.0.٣")]
    [InlineData("１.0.0")]
    [InlineData("1.0.0-bétà")]
    [InlineData("1234567890.0.0")]
    [InlineData("1.0.0-1234567890")]
    [InlineData("no version")]
    public void Anything_else_is_refused(string input)
    {
        SemVersion.TryParse(input, out _).Should().BeFalse(input);
        var parse = () => SemVersion.Parse(input);
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void A_text_longer_than_the_limit_is_refused()
    {
        var input = "1.0.0-" + new string('a', SemVersion.MaxLength);

        SemVersion.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Nine_digit_numbers_are_the_largest_taken()
    {
        SemVersion.TryParse("999999999.0.0-999999999", out var version).Should().BeTrue();
        version!.Major.Should().Be(999_999_999);
    }

    [Theory]
    [InlineData("v0.16.0-beta.1", "0.16.0-beta.1")]
    [InlineData("v1.0.0", "1.0.0")]
    public void A_release_tag_is_v_and_a_version(string tag, string version)
    {
        SemVersion.TryParseTag(tag, out var parsed).Should().BeTrue();
        parsed.Should().Be(SemVersion.Parse(version));
    }

    [Theory]
    [InlineData("0.16.0")]
    [InlineData("V0.16.0")]
    [InlineData("vv0.16.0")]
    [InlineData("v")]
    [InlineData("release-0.16.0")]
    [InlineData("v0.16.0+build")]
    public void Other_tags_are_refused(string tag)
    {
        SemVersion.TryParseTag(tag, out _).Should().BeFalse(tag);
    }
}
