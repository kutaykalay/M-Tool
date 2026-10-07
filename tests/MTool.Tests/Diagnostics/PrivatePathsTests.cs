using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

public sealed class PrivatePathsTests
{
    private const string Profile = @"C:\Users\Kutay";
    private static readonly PrivatePaths Paths = new(Profile);

    [Theory]
    [InlineData(@"Report written: C:\Users\Kutay\AppData\Roaming\M-Tool\reports\r.zip",
                @"Report written: %UserProfile%\AppData\Roaming\M-Tool\reports\r.zip")]
    [InlineData(@"c:\users\KUTAY\AppData\x.json", @"%UserProfile%\AppData\x.json")]
    [InlineData(@"C:/Users/Kutay/AppData/x.json", @"%UserProfile%/AppData/x.json")]
    [InlineData(@"\\?\C:\Users\Kutay\AppData\x.json", @"\\?\%UserProfile%\AppData\x.json")]
    [InlineData(@"""C:\\Users\\Kutay\\AppData\\x.json""", @"""%UserProfile%\\AppData\\x.json""")]
    [InlineData(@"Folder: C:\Users\Kutay", @"Folder: %UserProfile%")]
    [InlineData(@"Folder: C:\Users\Kutay.", @"Folder: %UserProfile%.")]
    [InlineData(@"(C:\Users\Kutay)", @"(%UserProfile%)")]
    [InlineData(@"C:\Users\Kutay: access denied", @"%UserProfile%: access denied")]
    [InlineData(@"'C:\Users\Kutay', 'C:\Users\Kutay\x'", @"'%UserProfile%', '%UserProfile%\x'")]
    [InlineData(@"C:\Users\Kutay; retry", @"%UserProfile%; retry")]
    public void Masks_the_profile_folder(string text, string expected) =>
        Paths.Mask(text).Should().Be(expected);

    [Theory]
    [InlineData(@"C:\Users\Kutay2\x")]
    [InlineData(@"C:\Users\Kutay.old\x")]
    [InlineData(@"C:\Users\Kutay-backup\x")]
    [InlineData(@"C:\Users\Kutay_1\x")]
    [InlineData(@"D:\Users\Kutay\x")]
    [InlineData(@"Kutay changed the fan curve")]
    public void Leaves_other_folders_and_plain_words_alone(string text) =>
        Paths.Mask(text).Should().Be(text);

    [Fact]
    public void Masks_every_occurrence_across_lines_of_an_exception()
    {
        var text = string.Join(Environment.NewLine,
            @"System.IO.IOException: C:\Users\Kutay\AppData\Roaming\M-Tool\settings.json is locked",
            @"   at MTool.Core.Settings.SettingsStore.Load() in C:\Users\Kutay\src\SettingsStore.cs:line 42");

        var masked = Paths.Mask(text);

        masked.Should().NotContain("Kutay");
        masked.Should().Contain(@"%UserProfile%\AppData\Roaming\M-Tool\settings.json is locked");
        masked.Should().Contain(@"in %UserProfile%\src\SettingsStore.cs:line 42");
    }

    [Theory]
    [InlineData(@"C:\Users\Kutay\")]
    [InlineData(@"C:/Users/Kutay")]
    public void Accepts_the_profile_folder_in_either_spelling(string profile) =>
        new PrivatePaths(profile).Mask(@"C:\Users\Kutay\x").Should().Be(@"%UserProfile%\x");

    [Fact]
    public void Masks_a_domain_profile_but_not_the_local_one_with_the_same_name()
    {
        var domain = new PrivatePaths(@"C:\Users\kutay.CORP");

        domain.Mask(@"C:\Users\kutay.CORP\x").Should().Be(@"%UserProfile%\x");
        domain.Mask(@"C:\Users\kutay\x").Should().Be(@"C:\Users\kutay\x");
    }

    [Fact]
    public void Masks_a_roaming_profile_on_a_server()
    {
        var roaming = new PrivatePaths(@"\\server\profiles\kutay");

        roaming.Mask(@"Saved to \\server\profiles\kutay\AppData\x").Should().Be(@"Saved to %UserProfile%\AppData\x");
    }

    [Fact]
    public void Matches_non_ASCII_names_without_regard_to_case()
    {
        var turkish = new PrivatePaths(@"C:\Users\Gökçe");

        turkish.Mask(@"C:\Users\GÖKÇE\x").Should().Be(@"%UserProfile%\x");
        turkish.Mask(@"C:\Users\Gökçe\x").Should().Be(@"%UserProfile%\x");
    }

    [Fact]
    public void Masks_a_name_with_a_space_up_to_the_end_of_the_folder()
    {
        var spaced = new PrivatePaths(@"C:\Users\Kutay Kalay");

        spaced.Mask(@"Saved to C:\Users\Kutay Kalay\x and C:\Users\Kutay Kalay done")
            .Should().Be(@"Saved to %UserProfile%\x and %UserProfile% done");
        spaced.Mask(@"C:\Users\Kutay\x").Should().Be(@"C:\Users\Kutay\x");
    }

    [Fact]
    public void Keeps_dollar_signs_in_the_rest_of_the_text()
    {
        Paths.Mask(@"C:\Users\Kutay\$1\x").Should().Be(@"%UserProfile%\$1\x");
    }

    [Fact]
    public void Treats_regex_characters_in_the_name_literally()
    {
        var odd = new PrivatePaths(@"C:\Users\a+b (x)");

        odd.Mask(@"C:\Users\a+b (x)\y").Should().Be(@"%UserProfile%\y");
        odd.Mask(@"C:\Users\aab (x)\y").Should().Be(@"C:\Users\aab (x)\y");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\")]
    [InlineData("C:")]
    [InlineData(@"\")]
    public void Masks_nothing_when_the_profile_folder_is_unknown_or_a_drive_root(string? profile) =>
        new PrivatePaths(profile).Mask(@"C:\Users\Kutay\x").Should().Be(@"C:\Users\Kutay\x");
}
