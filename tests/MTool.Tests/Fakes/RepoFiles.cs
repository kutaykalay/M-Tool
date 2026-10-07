namespace MTool.Tests.Fakes;

/// <summary>Source files of the repository, for tests that check the code itself rather than its behaviour.</summary>
internal static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    /// <summary>Files under <paramref name="folder"/> (relative to the root), build output left out.</summary>
    public static IEnumerable<string> Under(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), pattern, SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string file)
    {
        var parts = file.Split(Path.DirectorySeparatorChar);
        return parts.Contains("obj") || parts.Contains("bin");
    }

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MTool.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("The tests run outside the repository: MTool.slnx not found.");
    }
}
