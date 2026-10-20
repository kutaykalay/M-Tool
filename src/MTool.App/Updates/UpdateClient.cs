using System.Globalization;
using System.Net.Http;
using MTool.App.Net;
using MTool.Core.Updates;

namespace MTool.App.Updates;

/// <summary>Asks GitHub for the newest release; every way it can end is an outcome.</summary>
internal interface IUpdateClient
{
    /// <param name="stop">Cancelled when M-Tool closes.</param>
    Task<UpdateCheckOutcome> CheckAsync(CancellationToken stop);
}

/// <summary>
/// One GET of <see cref="UpdateEndpoint"/>, without credentials or cookies and following no redirect
/// (<see cref="SafeHttp"/>). At most <see cref="MaxAnswerBytes"/> of the answer is read, and only
/// through <see cref="UpdateCheckOutcome"/>. Never throws for a network problem.
/// </summary>
internal sealed class UpdateClient : IUpdateClient, IDisposable
{
    /// <summary>Ten releases with their notes and assets fit with room to spare.</summary>
    public const int MaxAnswerBytes = 256 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private const string ApiVersion = "2022-11-28";

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly SemVersion _current;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;

    /// <param name="handler">The real one from <see cref="SafeHttp.CreateHandler"/>, or a fake in tests; disposed with the client.</param>
    /// <param name="appVersion">The informational version, named in the User-Agent as GitHub asks.</param>
    /// <param name="current">This build's version, compared with the releases.</param>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> is not https.</exception>
    public UpdateClient(HttpMessageHandler handler, Uri endpoint, string appVersion, SemVersion current, TimeSpan timeout, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The update check goes over https only.", nameof(endpoint));
        }

        (_endpoint, _current, _timeout, _time) = (endpoint, current, timeout, time);
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd($"M-Tool/{appVersion}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    public async Task<UpdateCheckOutcome> CheckAsync(CancellationToken stop)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);

            // Headers only: the body is read by SafeHttp.ReadStartAsync, never more than MaxAnswerBytes of it.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var answer = await SafeHttp.ReadStartAsync(response.Content, MaxAnswerBytes, deadline.Token).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            var limitUsedUp = Header(response, "X-RateLimit-Remaining") == "0";
            return UpdateCheckOutcome.FromResponse(
                (int)response.StatusCode, SafeHttp.RetryAfter(response, now) ?? UntilReset(response, now), limitUsedUp, answer, _current);
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckOutcome.WithoutAnswer(stop.IsCancellationRequested ? UpdateStatus.Cancelled : UpdateStatus.Timeout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // As in SubmissionClient: the message is not kept, and a failure while closing is the close.
            return UpdateCheckOutcome.WithoutAnswer(stop.IsCancellationRequested ? UpdateStatus.Cancelled : UpdateStatus.Network);
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>GitHub's <c>X-RateLimit-Reset</c> is a Unix time in seconds; anything else is ignored.</summary>
    private static TimeSpan? UntilReset(HttpResponseMessage response, DateTimeOffset now)
    {
        if (!long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds) - now;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;
}
