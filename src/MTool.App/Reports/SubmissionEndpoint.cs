namespace MTool.App.Reports;

/// <summary>
/// Where "Send EC data" posts: the M-Tool server (a Cloudflare Worker that opens an issue in a private
/// repository). A constant, never read from settings or a file. <c>example.invalid</c> cannot resolve,
/// so until the server is deployed a send fails as "no network" and nothing leaves the laptop.
/// </summary>
internal static class SubmissionEndpoint
{
    public static Uri Uri { get; } = new("https://example.invalid/v1/submissions");
}
