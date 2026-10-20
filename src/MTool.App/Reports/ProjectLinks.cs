namespace MTool.App.Reports;

/// <summary>
/// The fixed pages of M-Tool on GitHub that the About tab opens. Nothing here is read from a setting,
/// a file or an answer from the network; the launcher opens only these addresses and the issue form.
/// </summary>
internal static class ProjectLinks
{
    public const string Host = "github.com";
    public const string RepositoryPath = "/kutaykalay/M-Tool";
    public const string ReleasesPath = RepositoryPath + "/releases";

    public static Uri Repository { get; } = Page(RepositoryPath);

    public static Uri Releases { get; } = Page(ReleasesPath);

    public static IReadOnlyList<Uri> All { get; } = Array.AsReadOnly([Repository, Releases]);

    /// <summary>
    /// Exactly one of <see cref="All"/>: https, github.com, the default port, no user name, no query,
    /// no fragment, and the path compared case by case (GitHub paths after the host are not ours to fold).
    /// </summary>
    public static bool IsAllowed(Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == Uri.UriSchemeHttps
        && url.UserInfo.Length == 0
        && url.IsDefaultPort
        && url.Query.Length == 0
        && url.Fragment.Length == 0
        && string.Equals(url.Host, Host, StringComparison.Ordinal)
        && All.Any(link => string.Equals(url.AbsolutePath, link.AbsolutePath, StringComparison.Ordinal));

    private static Uri Page(string path) => new UriBuilder(Uri.UriSchemeHttps, Host) { Path = path }.Uri;
}
