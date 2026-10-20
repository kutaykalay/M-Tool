using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace MTool.App.Net;

/// <summary>
/// What every request of M-Tool shares (the EC data submission and the update check): a handler that
/// follows no redirect and sends nothing of the user, and reads that stop at a fixed size.
/// </summary>
internal static class SafeHttp
{
    private const int MaxResponseHeadersKilobytes = 8;

    /// <summary>
    /// The handler used outside tests: no redirects, no cookies, no decompression, small headers, and the
    /// system proxy without the user's credentials (M-Tool runs elevated; it never signs in anywhere).
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        MaxResponseHeadersLength = MaxResponseHeadersKilobytes,
        UseProxy = true,
        DefaultProxyCredentials = null,
        Credentials = null,
        PreAuthenticate = false,
    };

    /// <summary>At most <paramref name="maxBytes"/> of the answer, as UTF-8; the rest is never read.</summary>
    public static async Task<string> ReadStartAsync(HttpContent content, int maxBytes, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[maxBytes];
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

    /// <summary>The server's <c>Retry-After</c> as a wait from <paramref name="now"/>; null when absent.</summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - now,
        _ => null,
    };
}
