using System.Net;
using System.Net.Http;
using System.Text;
using MTool.App.Reports;
using MTool.Core.Diagnostics;
using MTool.Tests.Fakes;

namespace MTool.Tests.Reports;

public sealed class SubmissionClientTests
{
    private const string Body = """{"schemaVersion": 1, "model": "P65 İş"}""";

    private readonly FakeHttpHandler _handler = new();

    private SubmissionClient Client(TimeSpan? timeout = null) =>
        new(_handler, SubmissionEndpoint.Uri, "0.12.0-beta.1+abc1234", timeout ?? TimeSpan.FromSeconds(20));

    [Fact]
    public async Task Sends_one_POST_with_the_exact_body_to_the_endpoint()
    {
        using var client = Client();

        var outcome = await client.SendAsync(Body, CancellationToken.None);

        outcome.Should().Be(new SubmissionOutcome(SubmissionStatus.Created, 201, "0123456789ab"));
        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(SubmissionEndpoint.Uri);
        request.Body.Should().Equal(Encoding.UTF8.GetBytes(Body));
        request.ContentType.Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task Names_M_Tool_and_carries_no_credentials_or_cookies()
    {
        using var client = Client();

        await client.SendAsync(Body, CancellationToken.None);

        var headers = _handler.Requests.Single().Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value), StringComparer.OrdinalIgnoreCase);
        headers["User-Agent"].Should().Be("M-Tool/0.12.0-beta.1+abc1234");
        headers.Keys.Should().NotContain(["Authorization", "Cookie", "Proxy-Authorization"]);
    }

    [Fact]
    public void The_endpoint_is_https_and_the_submission_path()
    {
        SubmissionEndpoint.Uri.Scheme.Should().Be(Uri.UriSchemeHttps);
        SubmissionEndpoint.Uri.AbsolutePath.Should().Be("/v1/submissions");
        SubmissionEndpoint.Uri.Query.Should().BeEmpty();
    }

    [Fact]
    public void The_real_handler_follows_no_redirect_and_keeps_no_cookie()
    {
        using var handler = SubmissionClient.CreateHandler();

        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseCookies.Should().BeFalse();
    }

    [Fact]
    public async Task A_redirect_is_not_followed()
    {
        _handler.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/") } });
        using var client = Client();

        var outcome = await client.SendAsync(Body, CancellationToken.None);

        outcome.Status.Should().Be(SubmissionStatus.Unavailable);
        _handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Rate_limited_reads_the_wait_in_seconds()
    {
        _handler.Answer = (_, _) =>
        {
            var response = FakeHttpHandler.Json((HttpStatusCode)429, """{"status":"error","error":"rate_limited"}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return Task.FromResult(response);
        };
        using var client = Client();

        var outcome = await client.SendAsync(Body, CancellationToken.None);

        outcome.Should().Be(new SubmissionOutcome(SubmissionStatus.RateLimited, 429, ErrorCode: "rate_limited", RetryAfter: TimeSpan.FromSeconds(90)));
    }

    [Fact]
    public async Task Rate_limited_reads_the_wait_as_a_date()
    {
        _handler.Answer = (_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(5));
            return Task.FromResult(response);
        };
        using var client = Client();

        var outcome = await client.SendAsync(Body, CancellationToken.None);

        outcome.RetryAfter.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Reads_at_most_4_KB_of_the_answer()
    {
        var huge = """{"status":"created","id":"0123456789ab","pad":" """ + new string('x', 100_000) + "\"}";
        _handler.Answer = (_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.Created, huge));
        using var client = Client();

        var outcome = await client.SendAsync(Body, CancellationToken.None);

        outcome.Should().Be(new SubmissionOutcome(SubmissionStatus.Created, 201)); // cut, so not understood: no id
    }

    [Fact]
    public async Task No_network_is_network()
    {
        _handler.Answer = (_, _) => throw new HttpRequestException("No such host is known.");
        using var client = Client();

        (await client.SendAsync(Body, CancellationToken.None)).Should().Be(SubmissionOutcome.WithoutAnswer(SubmissionStatus.Network));
    }

    [Fact]
    public async Task A_connection_lost_while_reading_the_answer_is_network()
    {
        _handler.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StreamContent(new BrokenStream()) });
        using var client = Client();

        (await client.SendAsync(Body, CancellationToken.None)).Should().Be(SubmissionOutcome.WithoutAnswer(SubmissionStatus.Network));
    }

    [Fact]
    public async Task No_answer_in_time_is_timeout()
    {
        _handler.Answer = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var client = Client(TimeSpan.FromMilliseconds(50));

        (await client.SendAsync(Body, CancellationToken.None)).Should().Be(SubmissionOutcome.WithoutAnswer(SubmissionStatus.Timeout));
    }

    [Fact]
    public async Task Stopped_by_M_Tool_is_cancelled()
    {
        using var stop = new CancellationTokenSource();
        _handler.Answer = async (_, token) =>
        {
            await stop.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var client = Client();

        (await client.SendAsync(Body, stop.Token)).Should().Be(SubmissionOutcome.WithoutAnswer(SubmissionStatus.Cancelled));
    }

    [Fact]
    public void Cannot_be_made_without_a_handler() =>
        FluentActions.Invoking(() => new SubmissionClient(null!, SubmissionEndpoint.Uri, "1", TimeSpan.FromSeconds(1)))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void Refuses_an_address_that_is_not_https() =>
        FluentActions.Invoking(() => new SubmissionClient(_handler, new Uri("http://example.invalid/v1/submissions"), "1", TimeSpan.FromSeconds(1)))
            .Should().Throw<ArgumentException>();

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("The connection was reset.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("The connection was reset.");
    }
}
