using System.Diagnostics;
using MTool.App.Reports;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

/// <summary>Only the refusals and the shape of the process: nothing here starts a process.</summary>
public sealed class ShellLauncherTests
{
    private const string Form = "https://github.com/kutaykalay/M-Tool/issues/new";

    [Theory]
    [InlineData("http://github.com/kutaykalay/M-Tool/issues/new")]
    [InlineData("https://evil.example/kutaykalay/M-Tool/issues/new")]
    [InlineData("https://github.com.evil.example/kutaykalay/M-Tool/issues/new")]
    [InlineData("https://github.com/someone/else/issues/new")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:about")]
    public void Anything_but_the_issue_form_of_M_Tool_is_refused(string address)
    {
        var open = () => new ShellLauncher().OpenUrl(new Uri(address));

        open.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_relative_address_is_refused()
    {
        var open = () => new ShellLauncher().OpenUrl(new Uri("issues/new", UriKind.Relative));

        open.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("https://user@github.com/kutaykalay/M-Tool/issues/new")]
    [InlineData("https://user:pw@github.com/kutaykalay/M-Tool/issues/new")]
    [InlineData("https://github.com:8443/kutaykalay/M-Tool/issues/new")]
    [InlineData(Form + "#section")]
    [InlineData(Form + "?template=device-report.yml#x")]
    [InlineData(Form + "?labels=bug")]
    [InlineData(Form + "?template=device-report.yml&assignees=me")]
    [InlineData(Form + "?template=device-report.yml&body=hello")]
    [InlineData(Form + "?template=device-report.yml&&title=x")]
    [InlineData(Form + "?=x")]
    [InlineData(Form + "?Title=x")]
    public void An_address_with_more_than_the_known_fields_is_refused(string address)
    {
        Refuses(address);
    }

    private static void Refuses(string address) =>
        ShellLauncher.IsIssueForm(new Uri(address)).Should().BeFalse(address);

    private static void Accepts(string address) =>
        ShellLauncher.IsIssueForm(new Uri(address)).Should().BeTrue(address);

    [Theory]
    [InlineData(Form)]
    [InlineData(Form + "?template=device-report.yml")]
    [InlineData(Form + "?template=device-report.yml&title=%5BDevice%20report%5D%20P65&model=P65&firmware=16Q4EMS2.107")]
    [InlineData("https://github.com:443/kutaykalay/M-Tool/issues/new?template=device-report.yml")]
    [InlineData("HTTPS://GITHUB.COM/kutaykalay/M-Tool/issues/new?model=P65")]
    public void The_known_form_and_fields_are_accepted(string address) => Accepts(address);

    [Fact]
    public void Every_address_the_app_builds_is_accepted()
    {
        ShellLauncher.IsIssueForm(IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107")).Should().BeTrue();
        ShellLauncher.IsIssueForm(IssueUrl.Build(null, null)).Should().BeTrue();
    }

    [Fact]
    public void The_browser_is_opened_by_the_shell_with_the_address_alone()
    {
        var url = IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107");

        ProcessStartInfo start = ShellLauncher.BrowserStartInfo(url);

        // Not explorer.exe: from the elevated process it opened the Documents folder instead of the page.
        start.FileName.Should().Be(url.AbsoluteUri);
        start.UseShellExecute.Should().BeTrue();
        start.Arguments.Should().BeEmpty();
        start.ArgumentList.Should().BeEmpty();
    }

    [Fact]
    public void No_process_is_prepared_for_an_address_that_is_not_the_form()
    {
        var prepare = () => ShellLauncher.BrowserStartInfo(new Uri("https://evil.example/x"));

        prepare.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_refused_before_anything_starts()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mtool-no-such-folder-" + Guid.NewGuid().ToString("N"));

        var open = () => new ShellLauncher().OpenFolder(missing);

        open.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void The_issue_form_is_opened_through_the_desktop_shell_and_nothing_else()
    {
        var shell = new Recorder(desktopOpens: true);
        var url = IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107");

        shell.Launcher().OpenUrl(url);

        shell.Desktop.Should().Equal(url.AbsoluteUri);
        shell.Direct.Should().BeEmpty();
    }

    [Fact]
    public void Without_the_desktop_shell_the_form_is_opened_directly_and_that_is_logged()
    {
        var shell = new Recorder(desktopOpens: false);
        var url = IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107");

        shell.Launcher().OpenUrl(url);

        shell.Direct.Should().Equal(url.AbsoluteUri);
        shell.Log.Lines.Should().ContainSingle(l => l.StartsWith("WARN") && l.Contains("desktop shell"));
    }

    [Fact]
    public void A_desktop_shell_that_fails_to_open_it_is_not_followed_by_an_elevated_open()
    {
        var direct = new List<string>();
        var launcher = new ShellLauncher(_ => throw new System.Runtime.InteropServices.COMException("no browser"), direct.Add);

        var open = () => launcher.OpenUrl(IssueUrl.Build("P65 Creator 9SE", "16Q4EMS2.107"));

        open.Should().Throw<System.Runtime.InteropServices.COMException>();
        direct.Should().BeEmpty();
    }

    [Fact]
    public void A_refused_address_reaches_neither_way()
    {
        var shell = new Recorder(desktopOpens: true);

        var open = () => shell.Launcher().OpenUrl(new Uri("https://evil.example/x"));

        open.Should().Throw<ArgumentException>();
        (shell.Desktop.Count, shell.Direct.Count).Should().Be((0, 0));
    }

    [Fact]
    public void A_folder_is_opened_through_the_desktop_shell_by_its_full_path()
    {
        var shell = new Recorder(desktopOpens: true);
        var folder = Path.GetTempPath();

        shell.Launcher().OpenFolder(folder);

        shell.Desktop.Should().Equal(Path.GetFullPath(folder));
        shell.Direct.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_folder_reaches_neither_way()
    {
        var shell = new Recorder(desktopOpens: true);
        var missing = Path.Combine(Path.GetTempPath(), "mtool-no-such-folder-" + Guid.NewGuid().ToString("N"));

        var open = () => shell.Launcher().OpenFolder(missing);

        open.Should().Throw<DirectoryNotFoundException>();
        (shell.Desktop.Count, shell.Direct.Count).Should().Be((0, 0));
    }

    /// <summary>Stands in for both ways of opening; nothing is started.</summary>
    private sealed class Recorder(bool desktopOpens)
    {
        public List<string> Desktop { get; } = [];

        public List<string> Direct { get; } = [];

        public ListLog Log { get; } = new();

        public ShellLauncher Launcher() => new(
            target =>
            {
                Desktop.Add(target);
                return desktopOpens;
            },
            Direct.Add,
            Log);
    }
}
