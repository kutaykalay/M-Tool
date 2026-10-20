using MTool.Core.Updates;

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
    public const string ReleaseTagPrefix = ReleasesPath + "/tag/";

    public static Uri Repository { get; } = Page(RepositoryPath);

    public static Uri Releases { get; } = Page(ReleasesPath);

    public static IReadOnlyList<Uri> All { get; } = Array.AsReadOnly([Repository, Releases]);

    /// <summary>The page of one release, made here from a checked version: never an address from GitHub's answer.</summary>
    public static Uri ReleaseTag(SemVersion version) => Page($"{ReleaseTagPrefix}v{version}");

    /// <summary>
    /// Exactly one of <see cref="All"/> or a <see cref="ReleaseTag"/> page: https, github.com, the default port, no user name, no query,
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
        && (All.Any(link => string.Equals(url.AbsolutePath, link.AbsolutePath, StringComparison.Ordinal)) || IsReleaseTag(url.AbsolutePath));

    /// <summary>The tag after the prefix must be a version tag as a whole: no further segment, no escape.</summary>
    private static bool IsReleaseTag(string path) =>
        path.StartsWith(ReleaseTagPrefix, StringComparison.Ordinal)
        && SemVersion.TryParseTag(path[ReleaseTagPrefix.Length..], out _);

    private static Uri Page(string path) => new UriBuilder(Uri.UriSchemeHttps, Host) { Path = path }.Uri;
}
