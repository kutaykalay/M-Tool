using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

public sealed class SubmissionOutcomeTests
{
    [Fact]
    public void Created_carries_the_id()
    {
        var outcome = SubmissionOutcome.FromResponse(201, null, """{"status":"created","id":"0123456789ab"}""");

        outcome.Should().Be(new SubmissionOutcome(SubmissionStatus.Created, HttpStatus: 201, Id: "0123456789ab"));
    }

    [Fact]
    public void Duplicate_carries_the_id() =>
        SubmissionOutcome.FromResponse(200, null, """{"status":"duplicate","id":"abcdef012345"}""")
            .Should().Be(new SubmissionOutcome(SubmissionStatus.Duplicate, HttpStatus: 200, Id: "abcdef012345"));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"status":"created"}""")]
    [InlineData("""{"status":"created","id":42}""")]
    [InlineData("""{"status":"created","id":"0123"}""")]
    [InlineData("""{"status":"created","id":"0123456789ABCDEF"}""")]
    [InlineData("""{"status":"created","id":"0123456789abcdef0"}""")]
    [InlineData("""{"status":"created","id":"012345\n6789ab"}""")]
    [InlineData("""{"status":"created","id":"<script>alert</script>"}""")]
    [InlineData("""["created"]""")]
    public void A_success_with_a_body_that_is_not_understood_is_still_created_without_an_id(string body)
    {
        var outcome = SubmissionOutcome.FromResponse(201, null, body);

        outcome.Status.Should().Be(SubmissionStatus.Created);
        outcome.Id.Should().BeNull();
    }

    [Fact]
    public void Too_many_requests_waits_as_long_as_the_server_says() =>
        SubmissionOutcome.FromResponse(429, TimeSpan.FromSeconds(120), """{"status":"error","error":"rate_limited"}""")
            .Should().Be(new SubmissionOutcome(SubmissionStatus.RateLimited, HttpStatus: 429, ErrorCode: "rate_limited", RetryAfter: TimeSpan.FromSeconds(120)));

    [Theory]
    [InlineData(null, 60)]
    [InlineData(-5, 60)]
    [InlineData(0, 60)]
    [InlineData(1, 1)]
    [InlineData(999_999, 3600)]
    public void Too_many_requests_without_a_usable_wait_waits_a_minute_and_never_over_an_hour(int? seconds, int expected) =>
        SubmissionOutcome.FromResponse(429, seconds is { } s ? TimeSpan.FromSeconds(s) : null, "")
            .RetryAfter.Should().Be(TimeSpan.FromSeconds(expected));

    [Theory]
    [InlineData(400, "bad_schema")]
    [InlineData(404, "not_found")]
    [InlineData(413, "too_large")]
    [InlineData(415, "unsupported_media_type")]
    public void A_client_error_is_rejected_with_the_server_code(int status, string code)
    {
        var outcome = SubmissionOutcome.FromResponse(status, null, $$"""{"status":"error","error":"{{code}}","path":"ec.fields[0]"}""");

        outcome.Should().Be(new SubmissionOutcome(SubmissionStatus.Rejected, HttpStatus: status, ErrorCode: code));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(100)]
    public void A_server_error_or_a_redirect_means_the_server_is_unavailable(int status) =>
        SubmissionOutcome.FromResponse(status, null, "").Should().Be(new SubmissionOutcome(SubmissionStatus.Unavailable, HttpStatus: status));

    [Theory]
    [InlineData("""{"error":"Bad_Code"}""")]
    [InlineData("""{"error":"bad code"}""")]
    [InlineData("""{"error":"C:\\Users\\Kutay"}""")]
    [InlineData("""{"error":7}""")]
    public void An_error_code_that_is_not_a_plain_code_is_dropped(string body) =>
        SubmissionOutcome.FromResponse(400, null, body).ErrorCode.Should().BeNull();

    [Fact]
    public void An_overlong_error_code_is_dropped() =>
        SubmissionOutcome.FromResponse(503, null, $$"""{"error":"{{new string('a', 65)}}"}""").ErrorCode.Should().BeNull();

    [Fact]
    public void A_deeply_nested_body_does_not_throw() =>
        SubmissionOutcome.FromResponse(500, null, new string('[', 5000)).Status.Should().Be(SubmissionStatus.Unavailable);

    [Theory]
    [InlineData(SubmissionStatus.Network)]
    [InlineData(SubmissionStatus.Timeout)]
    [InlineData(SubmissionStatus.Cancelled)]
    public void Outcomes_without_an_answer_have_no_status_code(SubmissionStatus status) =>
        SubmissionOutcome.WithoutAnswer(status).Should().Be(new SubmissionOutcome(status));

    [Fact]
    public void Only_outcomes_without_an_answer_can_be_made_without_one() =>
        FluentActions.Invoking(() => SubmissionOutcome.WithoutAnswer(SubmissionStatus.Created)).Should().Throw<ArgumentOutOfRangeException>();
}
