namespace MTool.Core.Updates;

public enum UpdateStatus
{
    /// <summary>The latest release is this version.</summary>
    UpToDate,

    /// <summary>A newer release is out: <see cref="UpdateCheckOutcome.Latest"/>.</summary>
    UpdateAvailable,

    /// <summary>This build is newer than every release (a development build).</summary>
    CurrentIsNewer,

    /// <summary>GitHub's limit for this address is used up; try again after <see cref="UpdateCheckOutcome.RetryAfter"/>.</summary>
    RateLimited,

    /// <summary>GitHub answered with an error or a redirect.</summary>
    Unavailable,

    /// <summary>A success code without a list of releases (a captive portal, a proxy page, no release yet).</summary>
    InvalidResponse,

    /// <summary>GitHub was not reached: no network, DNS, TLS, a proxy that wants a login.</summary>
    Network,

    /// <summary>No answer in time.</summary>
    Timeout,

    /// <summary>Stopped by M-Tool (closing).</summary>
    Cancelled,
}

/// <summary>
/// How an update check ended. Built only from the status code, the wait and a checked version: nothing
/// else of GitHub's answer is kept (<see cref="ReleaseFeed"/>).
/// </summary>
public sealed record UpdateCheckOutcome(UpdateStatus Status, int? HttpStatus = null, SemVersion? Latest = null, TimeSpan? RetryAfter = null)
{
    /// <summary>Waited when GitHub says too many but not for how long.</summary>
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private const int Forbidden = 403;
    private const int ProxyAuthenticationRequired = 407;
    private const int TooManyRequests = 429;

    /// <param name="status">The HTTP status code.</param>
    /// <param name="wait">From <c>Retry-After</c>, or until <c>X-RateLimit-Reset</c>; null when absent.</param>
    /// <param name="limitUsedUp"><c>X-RateLimit-Remaining</c> was 0: a 403 then means the hourly limit, not a refusal.</param>
    /// <param name="body">The start of the answer (the caller limits how much it reads).</param>
    /// <param name="current">This build's version; a pre-release build is also offered pre-releases (K3).</param>
    public static UpdateCheckOutcome FromResponse(int status, TimeSpan? wait, bool limitUsedUp, string body, SemVersion current) => status switch
    {
        >= 200 and < 300 => Compare(status, ReleaseFeed.Latest(body, includePrerelease: current.IsPrerelease), current),
        TooManyRequests => new(UpdateStatus.RateLimited, status, RetryAfter: Wait(wait)),
        Forbidden when limitUsedUp => new(UpdateStatus.RateLimited, status, RetryAfter: Wait(wait)),
        ProxyAuthenticationRequired => new(UpdateStatus.Network, status), // GitHub was never reached
        _ => new(UpdateStatus.Unavailable, status),
    };

    /// <summary>An outcome where GitHub never answered.</summary>
    public static UpdateCheckOutcome WithoutAnswer(UpdateStatus status) => status is UpdateStatus.Network or UpdateStatus.Timeout or UpdateStatus.Cancelled
        ? new(status)
        : throw new ArgumentOutOfRangeException(nameof(status), status, "Only Network, Timeout and Cancelled have no answer.");

    private static UpdateCheckOutcome Compare(int status, SemVersion? latest, SemVersion current) => latest switch
    {
        null => new(UpdateStatus.InvalidResponse, status),
        _ when latest > current => new(UpdateStatus.UpdateAvailable, status, latest),
        _ when latest == current => new(UpdateStatus.UpToDate, status, latest),
        _ => new(UpdateStatus.CurrentIsNewer, status, latest),
    };

    private static TimeSpan Wait(TimeSpan? wait) => wait is { } w && w > TimeSpan.Zero
        ? (w > MaxRetryAfter ? MaxRetryAfter : w)
        : DefaultRetryAfter;
}
