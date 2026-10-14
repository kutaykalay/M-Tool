namespace MTool.App.Reports;

/// <summary>
/// Where "Send EC data" posts: the M-Tool server, a Cloudflare Worker (repository kutaykalay/M-Tool-reports)
/// that checks the data and opens an issue in that private repository. A constant, never read from
/// settings or a file; changing it leaves older versions unable to send.
/// </summary>
internal static class SubmissionEndpoint
{
    public static Uri Uri { get; } = new("https://m-tool-reports.m-tool.workers.dev/v1/submissions");
}
