using System.Text.Json;
using System.Text.RegularExpressions;

namespace MTool.Core.Diagnostics;

public enum SubmissionStatus
{
    /// <summary>The server took the data.</summary>
    Created,

    /// <summary>The server already had data for this model, firmware and BIOS.</summary>
    Duplicate,

    /// <summary>Too many sends from this address; try again after <see cref="SubmissionOutcome.RetryAfter"/>.</summary>
    RateLimited,

    /// <summary>The server refused the data (4xx): an M-Tool too old or too new for it.</summary>
    Rejected,

    /// <summary>The server answered with an error, a redirect or something else it should not.</summary>
    Unavailable,

    /// <summary>The server was not reached: no network, DNS, TLS, a proxy that wants a login, or anything else that failed on the way.</summary>
    Network,

    /// <summary>No answer in time.</summary>
    Timeout,

    /// <summary>Stopped by M-Tool (closing).</summary>
    Cancelled,
}

/// <summary>
/// How a send of <see cref="EcSubmission"/> ended. Built only from what is safe to show and log: the
/// status code, an id that is plain lower-case hex and an error code that is a plain word; nothing else
/// of the server's answer is kept.
/// </summary>
public sealed partial record SubmissionOutcome(
    SubmissionStatus Status, int? HttpStatus = null, string? Id = null, string? ErrorCode = null, TimeSpan? RetryAfter = null)
{
    /// <summary>Waited when the server says too many but not for how long.</summary>
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private const int MaxDepth = 4;

    /// <param name="status">The HTTP status code.</param>
    /// <param name="retryAfter">The server's <c>Retry-After</c>, as a wait; null when absent.</param>
    /// <param name="body">The start of the answer (the caller limits how much it reads).</param>
    public static SubmissionOutcome FromResponse(int status, TimeSpan? retryAfter, string body)
    {
        var (answer, id, error) = Read(body);
        return status switch
        {
            // A success code alone is not the server: a captive portal or a proxy answers 200 with a page.
            >= 200 and < 300 when answer == "created" => new(SubmissionStatus.Created, status, Id: id),
            >= 200 and < 300 when answer == "duplicate" => new(SubmissionStatus.Duplicate, status, Id: id),
            407 => new(SubmissionStatus.Network, status), // the proxy wants a login, the server was never reached
            429 => new(SubmissionStatus.RateLimited, status, ErrorCode: error, RetryAfter: Wait(retryAfter)),
            >= 400 and < 500 => new(SubmissionStatus.Rejected, status, ErrorCode: error),
            _ => new(SubmissionStatus.Unavailable, status, ErrorCode: error),
        };
    }

    /// <summary>An outcome where the server never answered.</summary>
    public static SubmissionOutcome WithoutAnswer(SubmissionStatus status) => status is SubmissionStatus.Network or SubmissionStatus.Timeout or SubmissionStatus.Cancelled
        ? new(status)
        : throw new ArgumentOutOfRangeException(nameof(status), status, "Only Network, Timeout and Cancelled have no answer.");

    private static TimeSpan Wait(TimeSpan? retryAfter) => retryAfter is { } wait && wait > TimeSpan.Zero
        ? (wait > MaxRetryAfter ? MaxRetryAfter : wait)
        : DefaultRetryAfter;

    private static (string? Status, string? Id, string? Error) Read(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            var root = document.RootElement;
            return (Text(root, "status"), Match(Text(root, "id"), IdPattern()), Match(Text(root, "error"), ErrorPattern()));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Match(string? value, Regex pattern) => value is not null && pattern.IsMatch(value) ? value : null;

    [GeneratedRegex(@"\A[0-9a-f]{8,16}\z")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\A[a-z_]{1,64}\z")]
    private static partial Regex ErrorPattern();
}
