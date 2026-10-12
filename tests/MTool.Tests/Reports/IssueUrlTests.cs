using MTool.App.Reports;

namespace MTool.Tests.Reports;

public sealed class IssueUrlTests
{
    private static Dictionary<string, string> Fields(Uri url) =>
        url.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public void The_address_is_exactly_the_form_with_the_encoded_values()
    {
        var url = IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107");

        url.AbsoluteUri.Should().Be(
            "https://github.com/kutaykalay/M-Tool/issues/new?template=device-report.yml"
            + "&title=%5BDevice%20report%5D%20P65%20Creator%209SE&model=P65%20Creator%209SE&firmware=16Q4EMS2.107");
    }

    [Theory]
    [InlineData("P65 Creator 9SE", "16Q4EMS2.107")]
    [InlineData(null, null)]
    [InlineData("a&host=evil.example", "../../x")]
    [InlineData("https://evil.example/", "@evil.example")]
    public void Scheme_host_and_path_are_always_the_constants(string? model, string? firmware)
    {
        var url = IssueUrl.Build(model, firmware);

        url.Scheme.Should().Be("https");
        url.Host.Should().Be("github.com");
        url.AbsolutePath.Should().Be("/kutaykalay/M-Tool/issues/new");
        url.Port.Should().Be(443);
        url.UserInfo.Should().BeEmpty();
        url.Fragment.Should().BeEmpty();
    }

    [Fact]
    public void The_title_is_the_prefix_and_the_model()
    {
        var fields = Fields(IssueUrl.Build("P65 Creator 9SE", null));

        fields["title"].Should().Be("[Device report] P65 Creator 9SE");
        fields["model"].Should().Be("P65 Creator 9SE");
    }

    [Fact]
    public void An_injection_attempt_is_only_text_in_one_value()
    {
        var url = IssueUrl.Build("P65&labels=x#frag?x=1", "1.0&assignees=me");

        Fields(url).Keys.Should().BeEquivalentTo("template", "title", "model", "firmware");
        Fields(url)["model"].Should().Be("P65&labels=x#frag?x=1");
        Fields(url)["firmware"].Should().Be("1.0&assignees=me");
        url.AbsoluteUri.Should().NotContain("&labels=").And.NotContain("&assignees=").And.NotContain("#");
    }

    [Fact]
    public void Non_ascii_text_is_sent_as_utf8_percent_escapes()
    {
        var url = IssueUrl.Build("Dizüstü ğ 😀", null);

        url.AbsoluteUri.Should().Contain("model=Diz%C3%BCst%C3%BC%20%C4%9F%20%F0%9F%98%80");
        Fields(url)["model"].Should().Be("Dizüstü ğ 😀");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void An_unknown_model_leaves_out_the_model_and_the_title(string? model)
    {
        var fields = Fields(IssueUrl.Build(model, "16Q4EMS2.107"));

        fields.Keys.Should().BeEquivalentTo("template", "firmware");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void An_unknown_firmware_leaves_out_the_firmware(string? firmware)
    {
        var fields = Fields(IssueUrl.Build("P65 Creator 9SE", firmware));

        fields.Keys.Should().BeEquivalentTo("template", "title", "model");
    }

    [Fact]
    public void Nothing_known_is_only_the_template()
    {
        IssueUrl.Build(null, null).AbsoluteUri.Should().Be(
            "https://github.com/kutaykalay/M-Tool/issues/new?template=device-report.yml");
    }

    [Fact]
    public void Line_breaks_and_control_characters_do_not_reach_the_address()
    {
        var fields = Fields(IssueUrl.Build("  P65\r\nCreator\t\t9SE\0\u001b  ", null));

        fields["model"].Should().Be("P65 Creator 9SE");
    }

    [Theory]
    [InlineData(10_000, "x")]
    [InlineData(10_000, "ü")]
    [InlineData(5_000, "😀")]
    public void A_very_long_value_is_cut_and_the_address_stays_short(int length, string filler)
    {
        var long_ = string.Concat(Enumerable.Repeat(filler, length));

        var url = IssueUrl.Build(long_, long_);

        url.AbsoluteUri.Length.Should().BeLessThanOrEqualTo(IssueUrl.MaxUrlLength);
        var model = Fields(url)["model"];
        model.Should().NotBeEmpty().And.NotContain("�");
        long_.Should().StartWith(model);
        Fields(url)["firmware"].Should().NotBeEmpty();
    }

    [Fact]
    public void A_lone_surrogate_is_dropped_not_sent()
    {
        var fields = Fields(IssueUrl.Build("P65\uD800 Creator", null));

        fields["model"].Should().Be("P65 Creator");
    }
}
