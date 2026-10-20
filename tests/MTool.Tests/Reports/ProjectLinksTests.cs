using MTool.App.Reports;
using MTool.Core.Updates;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

/// <summary>The fixed project pages of the About tab, and the launcher's refusal of anything near them.</summary>
public sealed class ProjectLinksTests
{
    private const string Repo = "https://github.com/kutaykalay/M-Tool";

    [Fact]
    public void Every_project_link_is_accepted_and_opened_by_the_launcher()
    {
        var desktop = new List<string>();
        var launcher = new ShellLauncher(target =>
        {
            desktop.Add(target);
            return true;
        }, _ => { }, new FakeClipboard());

        foreach (var link in ProjectLinks.All)
        {
            ProjectLinks.IsAllowed(link).Should().BeTrue(link.AbsoluteUri);
            launcher.OpenUrl(link);
        }

        desktop.Should().Equal(ProjectLinks.All.Select(l => l.AbsoluteUri));
    }

    [Fact]
    public void The_links_are_the_repository_and_its_releases()
    {
        ProjectLinks.Repository.AbsoluteUri.Should().Be(Repo);
        ProjectLinks.Releases.AbsoluteUri.Should().Be(Repo + "/releases");
    }

    [Fact]
    public void The_issue_form_lives_in_the_same_repository()
    {
        IssueUrl.FormPath.Should().StartWith(ProjectLinks.RepositoryPath + "/");
    }

    [Theory]
    [InlineData("http://github.com/kutaykalay/M-Tool")]
    [InlineData("https://github.com.evil.example/kutaykalay/M-Tool")]
    [InlineData("https://evil.example/kutaykalay/M-Tool")]
    [InlineData("https://user@github.com/kutaykalay/M-Tool")]
    [InlineData("https://github.com:8443/kutaykalay/M-Tool")]
    [InlineData("https://github.com/kutaykalay/M-Tool?tab=readme")]
    [InlineData("https://github.com/kutaykalay/M-Tool#readme")]
    [InlineData("https://github.com/kutaykalay/M-Tool/")]
    [InlineData("https://github.com/KutayKalay/M-Tool")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/../../other")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/download/v1/M-Tool.exe")]
    [InlineData("https://github.com/kutaykalay/M-Tool/issues")]
    [InlineData("https://github.com/someone/M-Tool")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("https://github.com./kutaykalay/M-Tool")]
    [InlineData("https://xn--gthub-n4a.com/kutaykalay/M-Tool")]
    [InlineData("https://github.com:444/kutaykalay/M-Tool/releases")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/")]
    [InlineData("https://github.com/kutaykalay/m-tool/releases")]
    [InlineData("https://github.com/kutaykalay/M-Tool/%2e%2e/evil")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases?")]
    [InlineData("https://:@github.com/kutaykalay/M-Tool")]
    public void An_address_that_is_not_exactly_a_project_link_is_refused(string address)
    {
        var url = new Uri(address);

        ProjectLinks.IsAllowed(url).Should().BeFalse(address);
        var open = () => new ShellLauncher(_ => true, _ => { }, new FakeClipboard()).OpenUrl(url);
        open.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_relative_address_is_refused()
    {
        ProjectLinks.IsAllowed(new Uri("kutaykalay/M-Tool", UriKind.Relative)).Should().BeFalse();
    }

    [Theory]
    [InlineData("HTTPS://GITHUB.COM/kutaykalay/M-Tool")]
    [InlineData("https://github.com:443/kutaykalay/M-Tool/releases")]
    public void The_same_link_in_another_spelling_is_accepted(string address)
    {
        ProjectLinks.IsAllowed(new Uri(address)).Should().BeTrue(address);
    }

    [Theory]
    [InlineData("0.16.0-beta.1")]
    [InlineData("1.0.0")]
    public void A_release_page_is_built_here_from_a_checked_version(string version)
    {
        var page = ProjectLinks.ReleaseTag(SemVersion.Parse(version));

        page.AbsoluteUri.Should().Be($"{Repo}/releases/tag/v{version}");
        ProjectLinks.IsAllowed(page).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v0.16.0-beta.1")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v1.0.0")]
    public void A_release_page_with_a_valid_tag_is_accepted(string address)
    {
        ProjectLinks.IsAllowed(new Uri(address)).Should().BeTrue(address);
    }

    [Theory]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/0.16.0")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v0.16")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v0.16.0+x")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v0.16.0/../../../evil")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v0.16.0/assets")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v0.16.0?x=1")]
    [InlineData("https://github.com/kutaykalay/M-Tool/releases/tag/v0.16.0%2F..%2Fx")]
    [InlineData("https://github.com/other/M-Tool/releases/tag/v0.16.0")]
    public void A_release_page_with_anything_but_a_valid_tag_is_refused(string address)
    {
        ProjectLinks.IsAllowed(new Uri(address)).Should().BeFalse(address);
    }
}
