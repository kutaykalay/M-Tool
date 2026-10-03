using MTool.App.Services;
using MTool.App.Startup;

namespace MTool.Tests.Services;

public class StartupTaskSpecTests
{
    [Fact]
    public void The_task_starts_the_app_hidden_in_the_tray_shortly_after_sign_in()
    {
        StartupTaskSpec.TaskName.Should().Be("M-Tool");
        StartupTaskSpec.Arguments.Should().Be(StartupArgs.Tray);
        StartupTaskSpec.SignInDelay.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(@"C:\Program Files\M-Tool\M-Tool.exe", @"C:\Program Files\M-Tool\M-Tool.exe")]
    [InlineData(@"c:\program files\m-tool\m-tool.EXE", @"C:\Program Files\M-Tool\M-Tool.exe")]
    [InlineData(@"""C:\Program Files\M-Tool\M-Tool.exe""", @"C:\Program Files\M-Tool\M-Tool.exe")]
    [InlineData(@" C:\Program Files\M-Tool\M-Tool.exe ", @"C:\Program Files\M-Tool\M-Tool.exe")]
    [InlineData(@"C:\Program Files\M-Tool\..\M-Tool\M-Tool.exe", @"C:\Program Files\M-Tool\M-Tool.exe")]
    public void The_same_exe_written_differently_needs_no_repair(string registered, string current)
    {
        StartupTaskSpec.NeedsRepair(registered, current).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Users\PC\Downloads\M-Tool.exe")]
    [InlineData(@"C:\Program Files\M-Tool-old\M-Tool.exe")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\bad\0path.exe")]
    public void Another_or_a_missing_exe_needs_repair(string? registered)
    {
        StartupTaskSpec.NeedsRepair(registered, @"C:\Program Files\M-Tool\M-Tool.exe").Should().BeTrue();
    }
}
