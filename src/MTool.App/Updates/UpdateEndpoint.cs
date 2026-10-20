using MTool.App.Reports;

namespace MTool.App.Updates;

/// <summary>
/// GitHub's list of M-Tool releases. Fixed here, like <see cref="SubmissionEndpoint"/>: never read from
/// a setting or a file. Five releases are enough to find the newest stable one between betas, and
/// keep the answer well under <see cref="UpdateClient.MaxAnswerBytes"/> even with long release notes.
/// </summary>
internal static class UpdateEndpoint
{
    public static Uri Uri { get; } = new($"https://api.github.com/repos{ProjectLinks.RepositoryPath}/releases?per_page=5");
}
