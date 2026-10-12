using MTool.App.Reports;

namespace MTool.Tests.Reports;

/// <summary>Only the refusals: nothing here starts a process.</summary>
public sealed class ShellLauncherTests
{
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

    [Fact]
    public void A_folder_that_does_not_exist_is_refused_before_anything_starts()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mtool-no-such-folder-" + Guid.NewGuid().ToString("N"));

        var open = () => new ShellLauncher().OpenFolder(missing);

        open.Should().Throw<DirectoryNotFoundException>();
    }
}
