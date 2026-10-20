using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;
using MTool.App.Net;
using MTool.App.Updates;
using MTool.Core.Updates;
using MTool.Tests.Fakes;

namespace MTool.Tests.Updates;

/// <summary>The update check on the wire (Aşama 14b), against a fake handler: no real network.</summary>
public sealed class UpdateClientTests
{
    private const string Feed = """[{"tag_name":"v0.17.0-beta.1","draft":false,"prerelease":true}]""";

    private readonly FakeHttpHandler _handler = new() { Answer = (_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Feed)) };
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    private UpdateClient Client(TimeSpan? timeout = null) =>
        new(_handler, UpdateEndpoint.Uri, "0.16.0-beta.1+448e0a5", SemVersion.Parse("0.16.0-beta.1"), timeout ?? UpdateClient.DefaultTimeout, _time);

    [Fact]
    public void The_endpoint_is_the_fixed_https_release_list_of_M_Tool()
    {
        UpdateEndpoint.Uri.AbsoluteUri.Should().Be("https://api.github.com/repos/kutaykalay/M-Tool/releases?per_page=5");
    }

    [Fact]
    public async Task Sends_one_GET_that_names_M_Tool_and_the_API_version_and_nothing_private()
    {
        using var client = Client();

        var outcome = await client.CheckAsync(CancellationToken.None);

        outcome.Should().Be(new UpdateCheckOutcome(UpdateStatus.UpdateAvailable, 200, SemVersion.Parse("0.17.0-beta.1")));
        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Should().Be(UpdateEndpoint.Uri);
        request.Body.Should().BeNull();
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value), StringComparer.OrdinalIgnoreCase);
        headers["User-Agent"].Should().Be("M-Tool/0.16.0-beta.1+448e0a5");
        headers["Accept"].Should().Be("application/vnd.github+json");
        headers["X-GitHub-Api-Version"].Should().Be("2022-11-28");
        headers.Keys.Should().NotContain(["Authorization", "Cookie", "Proxy-Authorization"]);
    }

    [Fact]
    public void An_http_endpoint_is_refused()
    {
        var make = () => new UpdateClient(_handler, new Uri("http://api.github.com/x"), "1", SemVersion.Parse("1.0.0"), UpdateClient.DefaultTimeout, _time);

        make.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_hourly_limit_waits_until_its_reset()
    {
        _handler.Answer = (_, _) =>
        {
            var answer = FakeHttpHandler.Json(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""");
            answer.Headers.Add("X-RateLimit-Remaining", "0");
            answer.Headers.Add("X-RateLimit-Reset", _time.GetUtcNow().AddMinutes(17).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromResult(answer);
        };
        using var client = Client();

        var outcome = await client.CheckAsync(CancellationToken.None);

        outcome.Should().Be(new UpdateCheckOutcome(UpdateStatus.RateLimited, 403, RetryAfter: TimeSpan.FromMinutes(17)));
    }

    [Fact]
    public async Task Retry_after_is_taken_as_the_wait()
    {
        _handler.Answer = (_, _) =>
        {
            var answer = FakeHttpHandler.Json(HttpStatusCode.TooManyRequests, "");
            answer.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return Task.FromResult(answer);
        };
        using var client = Client();

        (await client.CheckAsync(CancellationToken.None)).RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("99999999999999999999")]
    public async Task A_reset_header_that_is_not_a_time_is_ignored(string reset)
    {
        _handler.Answer = (_, _) =>
        {
            var answer = FakeHttpHandler.Json(HttpStatusCode.Forbidden, "{}");
            answer.Headers.Add("X-RateLimit-Remaining", "0");
            answer.Headers.Add("X-RateLimit-Reset", reset);
            return Task.FromResult(answer);
        };
        using var client = Client();

        (await client.CheckAsync(CancellationToken.None)).RetryAfter.Should().Be(UpdateCheckOutcome.DefaultRetryAfter);
    }

    [Fact]
    public async Task Only_the_start_of_a_huge_answer_is_read()
    {
        var huge = """[{"tag_name":"v0.17.0-beta.1","draft":false,"prerelease":true,"body":" """ + new string('x', UpdateClient.MaxAnswerBytes) + "\"}]";
        _handler.Answer = (_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, huge));
        using var client = Client();

        (await client.CheckAsync(CancellationToken.None)).Status.Should().Be(UpdateStatus.InvalidResponse);
    }

    [Fact]
    public async Task No_network_is_a_network_outcome()
    {
        _handler.Answer = (_, _) => throw new HttpRequestException("No such host is known.");
        using var client = Client();

        (await client.CheckAsync(CancellationToken.None)).Should().Be(UpdateCheckOutcome.WithoutAnswer(UpdateStatus.Network));
    }

    [Fact]
    public async Task No_answer_in_time_is_a_timeout()
    {
        _handler.Answer = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpHandler.Json(HttpStatusCode.OK, Feed);
        };
        using var client = Client(TimeSpan.FromMilliseconds(50));

        (await client.CheckAsync(CancellationToken.None)).Should().Be(UpdateCheckOutcome.WithoutAnswer(UpdateStatus.Timeout));
    }

    [Fact]
    public async Task Closing_M_Tool_cancels_the_check()
    {
        using var stop = new CancellationTokenSource();
        _handler.Answer = async (_, token) =>
        {
            await stop.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpHandler.Json(HttpStatusCode.OK, Feed);
        };
        using var client = Client();

        (await client.CheckAsync(stop.Token)).Should().Be(UpdateCheckOutcome.WithoutAnswer(UpdateStatus.Cancelled));
    }
}
