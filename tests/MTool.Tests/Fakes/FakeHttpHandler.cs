using System.Net;
using MTool.App.Reports;

namespace MTool.Tests.Fakes;

/// <summary>The network without the network: records each request and answers with <see cref="Answer"/>.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
        (_, _) => Task.FromResult(Json(HttpStatusCode.Created, """{"status":"created","id":"0123456789ab"}"""));

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>A client on a new fake handler, for tests that never send.</summary>
    public static SubmissionClient Client() => new(new FakeHttpHandler(), SubmissionEndpoint.Uri, "0.9.0", SubmissionClient.DefaultTimeout);

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, request.Headers.ToList(),
            request.Content?.Headers.ContentType?.ToString(), body));
        return await Answer(request, cancellationToken);
    }

    internal sealed record RecordedRequest(
        HttpMethod Method, Uri? Uri, List<KeyValuePair<string, IEnumerable<string>>> Headers, string? ContentType, byte[]? Body);
}
