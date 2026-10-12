using MTool.App.Reports;

namespace MTool.Tests.Fakes;

/// <summary>Records what would have been opened; opens nothing.</summary>
internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> Folders { get; } = [];

    public List<Uri> Urls { get; } = [];

    public bool Fails { get; set; }

    public void OpenFolder(string folder)
    {
        Folders.Add(folder);
        if (Fails)
        {
            throw new InvalidOperationException(@"Explorer failed for C:\Users\secret\reports");
        }
    }

    public void OpenUrl(Uri url)
    {
        Urls.Add(url);
        if (Fails)
        {
            throw new InvalidOperationException("No browser");
        }
    }
}
