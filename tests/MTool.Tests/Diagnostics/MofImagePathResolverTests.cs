using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

/// <summary>The expansion of <c>MofImagePath</c> is pure: nothing here reads the environment or the disk.</summary>
public class MofImagePathResolverTests
{
    private const string WindowsDir = @"C:\Windows";

    [Theory]
    [InlineData(@"%windir%\sysWOW64\msiapcfg.dll", @"C:\Windows\sysWOW64\msiapcfg.dll")]
    [InlineData(@"%WINDIR%\sysWOW64\msiapcfg.dll", @"C:\Windows\sysWOW64\msiapcfg.dll")]
    [InlineData(@"%SystemRoot%\System32\x.dll", @"C:\Windows\System32\x.dll")]
    [InlineData(@"%systemroot%\System32\x.dll", @"C:\Windows\System32\x.dll")]
    [InlineData(@"\SystemRoot\System32\x.dll", @"C:\Windows\System32\x.dll")]
    [InlineData(@"\systemroot\System32\x.dll", @"C:\Windows\System32\x.dll")]
    [InlineData(@"D:\Drivers\x.dll", @"D:\Drivers\x.dll")]
    [InlineData(@"C:\Windows\System32\..\x.dll", @"C:\Windows\x.dll")]
    [InlineData(@"C:\Windows\console.dll", @"C:\Windows\console.dll")]
    [InlineData(@"C:\Windows\com10.dll", @"C:\Windows\com10.dll")]
    [InlineData(@"C:\Windows\CON\x.dll", @"C:\Windows\CON\x.dll")]
    public void Known_roots_and_plain_local_paths_resolve_to_a_full_path(string raw, string expected)
    {
        MofImagePathResolver.Resolve(raw, WindowsDir).Should().Be(expected);
    }

    [Fact]
    public void A_windows_directory_with_a_trailing_separator_does_not_double_it()
    {
        MofImagePathResolver.Resolve(@"%windir%\x.dll", @"C:\Windows\").Should().Be(@"C:\Windows\x.dll");
    }

    [Theory]
    [InlineData(@"%ProgramData%\x.dll")]
    [InlineData(@"%TEMP%\x.dll")]
    [InlineData(@"%windir%\%TEMP%\x.dll")]
    [InlineData(@"%windir%x.dll")]
    [InlineData(@"\\server\share\x.dll")]
    [InlineData(@"\\?\C:\Windows\x.dll")]
    [InlineData(@"\\.\C:\Windows\x.dll")]
    [InlineData(@"\??\C:\Windows\x.dll")]
    [InlineData(@"x.dll")]
    [InlineData(@"\Windows\x.dll")]
    [InlineData(@"C:x.dll")]
    [InlineData(@"C:\a:stream")]
    [InlineData(@"C:\Windows\CON")]
    [InlineData(@"C:\Windows\con.dll")]
    [InlineData(@"C:\Windows\NUL.tar.gz")]
    [InlineData(@"C:\Windows\Aux")]
    [InlineData(@"C:\Windows\prn.")]
    [InlineData(@"C:\Windows\prn . ")]
    [InlineData(@"C:\Windows\COM1.dll")]
    [InlineData(@"C:\Windows\com9")]
    [InlineData(@"C:\Windows\LPT3.txt")]
    [InlineData(@"%windir%\lpt9")]
    [InlineData("")]
    public void Anything_else_is_rejected(string raw)
    {
        MofImagePathResolver.Resolve(raw, WindowsDir).Should().BeNull();
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("")]
    [InlineData("Windows")]
    public void A_windows_directory_that_is_not_a_local_path_rejects_the_root_forms(string windowsDir)
    {
        MofImagePathResolver.Resolve(@"%windir%\x.dll", windowsDir).Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\Windows\x.dll", true)]
    [InlineData(@"c:/Windows/x.dll", true)]
    [InlineData(@"\\server\share\x.dll", false)]
    [InlineData(@"\\?\C:\x.dll", false)]
    [InlineData(@"C:x.dll", false)]
    [InlineData(@"1:\x.dll", false)]
    [InlineData("C:", false)]
    [InlineData(null, false)]
    public void Only_drive_letter_paths_are_local(string? path, bool expected)
    {
        MofImagePathResolver.IsLocalDrivePath(path).Should().Be(expected);
    }
}
