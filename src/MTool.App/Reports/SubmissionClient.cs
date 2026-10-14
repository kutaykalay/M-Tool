using System.IO;
using System.Net.Http;
using System.Text;
using MTool.Core.Diagnostics;

namespace MTool.App.Reports;

/// <summary>
/// Posts the <see cref="EcSubmission"/> text, exactly as the user saw it, once. No credentials, no
/// cookies, no redirects (the production handler follows none; a 3xx is "unavailable"). At most
/// <see cref="MaxAnswerBytes"/> of the answer is read, and only through <see cref="SubmissionOutcome"/>.
/// Never throws for a network problem: every way it can end is an outcome.
/// </summary>
internal sealed class SubmissionClient : IDisposable
{
    public const int MaxAnswerBytes = 4 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    /// <param name="handler">The real one from <see cref="CreateHandler"/>, or a fake in tests; disposed with the client.</param>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> is not https.</exception>
    public SubmissionClient(HttpMessageHandler handler, Uri endpoint, string appVersion, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The submission goes over https only.", nameof(endpoint));
        }

        _endpoint = endpoint;
        _timeout = timeout;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd($"M-Tool/{appVersion}");
    }

    /// <summary>The handler used outside tests: no redirects, no cookies, the system proxy.</summary>
    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    /// <param name="stop">Cancelled when M-Tool closes.</param>
    public async Task<SubmissionOutcome> SendAsync(string json, CancellationToken stop)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            // Headers only: the body is read by ReadStart, never more than MaxAnswerBytes of it.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var answer = await ReadStart(response.Content, deadline.Token).ConfigureAwait(false);
            return SubmissionOutcome.FromResponse((int)response.StatusCode, RetryAfter(response), answer);
        }
        catch (OperationCanceledException)
        {
            return SubmissionOutcome.WithoutAnswer(stop.IsCancellationRequested ? SubmissionStatus.Cancelled : SubmissionStatus.Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return SubmissionOutcome.WithoutAnswer(SubmissionStatus.Network);
        }
    }

    public void Dispose() => _http.Dispose();

    private static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow,
        _ => null,
    };

    private static async Task<string> ReadStart(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[MaxAnswerBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), token).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
