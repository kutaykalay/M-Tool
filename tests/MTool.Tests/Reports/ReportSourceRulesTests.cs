using System.Text.RegularExpressions;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

/// <summary>Rules about the report code itself, checked on the source files.</summary>
public sealed partial class ReportSourceRulesTests
{
    [GeneratedRegex(@"\bCreateWithWmi2\b")]
    private static partial Regex WithWmi2();

    private static string Relative(string file) => Path.GetRelativePath(RepoFiles.Root, file).Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public void Only_the_command_line_and_the_confirmed_tray_command_can_ask_for_the_wmi2_report()
    {
        var users = RepoFiles.Under("src", "*.cs")
            .Where(file => WithWmi2().IsMatch(File.ReadAllText(file)))
            .Select(Relative);

        users.Should().BeEquivalentTo(
            "src/MTool.App/Cli/CliRunner.cs",
            "src/MTool.App/ViewModels/ReportViewModel.cs",
            "src/MTool.App/Reports/ReportService.cs"); // the last one defines it
    }

    [Theory]
    [InlineData("src/MTool.App/Hardware/MsiWmi2Session.cs")]
    [InlineData("src/MTool.App/Hardware/Wmi2Live.cs")]
    [InlineData("src/MTool.App/Cli/ReportCommand.cs")]
    public void Log_and_report_text_in_these_files_is_english(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepoFiles.Root, file));

        text.Where(c => c > '\u007F').Should().BeEmpty("log lines and the public report are English");
    }
}
