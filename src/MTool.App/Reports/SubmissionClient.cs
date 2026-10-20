using System.Net.Http;
using System.Text;
using MTool.App.Net;
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

    /// <param name="handler">The real one from <see cref="SafeHttp.CreateHandler"/>, or a fake in tests; disposed with the client.</param>
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

            // Headers only: the body is read by SafeHttp.ReadStartAsync, never more than MaxAnswerBytes of it.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var answer = await SafeHttp.ReadStartAsync(response.Content, MaxAnswerBytes, deadline.Token).ConfigureAwait(false);
            return SubmissionOutcome.FromResponse((int)response.StatusCode, SafeHttp.RetryAfter(response, DateTimeOffset.UtcNow), answer);
        }
        catch (OperationCanceledException)
        {
            return SubmissionOutcome.WithoutAnswer(stop.IsCancellationRequested ? SubmissionStatus.Cancelled : SubmissionStatus.Timeout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // HttpRequestException and IOException in practice; anything else (a proxy scheme it cannot
            // use) must not reach the tray either. The message is not kept: it may hold a path or a host.
            // Once M-Tool is closing, a failure is the client being disposed under the request, not the network.
            return SubmissionOutcome.WithoutAnswer(stop.IsCancellationRequested ? SubmissionStatus.Cancelled : SubmissionStatus.Network);
        }
    }

    public void Dispose() => _http.Dispose();
}
