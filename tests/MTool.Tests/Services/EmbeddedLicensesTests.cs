using MTool.App.Services;
using MTool.Tests.Fakes;

namespace MTool.Tests.Services;

/// <summary>The license texts travel inside the exe, so the About tab shows them offline (Aşama 14, K5).</summary>
public sealed class EmbeddedLicensesTests
{
    [Fact]
    public void Both_files_are_in_the_exe_and_read_whole()
    {
        var text = EmbeddedLicenses.Read();

        text.Should().NotBeNull();
        text.Should().Contain("CommunityToolkit.Mvvm").And.Contain("PawnIO");
        text.Should().Contain("GNU GENERAL PUBLIC LICENSE").And.Contain("END OF TERMS AND CONDITIONS");
    }

    [Fact]
    public void The_notices_come_before_the_long_GPL_text()
    {
        var text = EmbeddedLicenses.Read()!;

        text.IndexOf("CommunityToolkit.Mvvm", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("GNU GENERAL PUBLIC LICENSE", StringComparison.Ordinal));
    }

    [Fact]
    public void The_texts_match_the_files_in_the_repository()
    {
        var root = RepoFiles.Root;
        var text = EmbeddedLicenses.Read()!;

        text.Should().Contain(Normalize(File.ReadAllText(Path.Combine(root, "LICENSE"))).Trim());
        text.Should().Contain(Normalize(File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"))).Trim());
    }

    private static string Normalize(string text) => text.ReplaceLineEndings(Environment.NewLine);
}
