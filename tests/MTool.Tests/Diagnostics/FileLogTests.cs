using MTool.App;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

public sealed class FileLogTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Writes_the_profile_folder_as_a_placeholder()
    {
        var log = new FileLog(_folder, new PrivatePaths(@"C:\Users\Kutay"));

        log.Info(@"Report written: C:\Users\Kutay\AppData\Roaming\M-Tool\reports\r.zip");

        var text = ReadLog();
        text.Should().Contain(@"INFO  Report written: %UserProfile%\AppData\Roaming\M-Tool\reports\r.zip");
        text.Should().NotContain("Kutay");
    }

    [Fact]
    public void Masks_warnings_too()
    {
        var log = new FileLog(_folder, new PrivatePaths(@"C:\Users\Kutay"));

        log.Warn(@"Settings file C:\Users\Kutay\AppData\Roaming\M-Tool\settings.json is damaged");

        ReadLog().Should().Contain(@"%UserProfile%\AppData\Roaming\M-Tool\settings.json is damaged").And.NotContain("Kutay");
    }

    [Fact]
    public void Masks_paths_inside_an_exception()
    {
        var log = new FileLog(_folder, new PrivatePaths(@"C:\Users\Kutay"));

        log.Error(@"Could not read settings", new IOException(@"C:\Users\Kutay\AppData\Roaming\M-Tool\settings.json is locked"));

        var text = ReadLog();
        text.Should().Contain(@"ERROR Could not read settings");
        text.Should().Contain(@"%UserProfile%\AppData\Roaming\M-Tool\settings.json is locked");
        text.Should().NotContain("Kutay");
    }

    private string ReadLog() => File.ReadAllText(Directory.GetFiles(_folder, "m-tool-*.log").Single());
}
