using MTool.Core.Updates;

namespace MTool.Tests.Updates;

/// <summary>
/// The GitHub releases list and how an answer becomes an outcome (Aşama 14b). Only <c>tag_name</c>,
/// <c>draft</c> and <c>prerelease</c> are read; nothing else of the answer is kept.
/// </summary>
public sealed class UpdateCheckOutcomeTests
{
    private static readonly SemVersion Beta = SemVersion.Parse("0.16.0-beta.1");
    private static readonly SemVersion Stable = SemVersion.Parse("1.0.0");

    private static string Release(string tag, bool prerelease = false, bool draft = false) =>
        $$$"""{"tag_name":"{{{tag}}}","draft":{{{(draft ? "true" : "false")}}},"prerelease":{{{(prerelease ? "true" : "false")}}},"name":"x","body":"<script>","html_url":"https://evil.example/","assets":[{"uploader":{"login":"a"}}]}""";

    private static string Feed(params string[] releases) => $"[{string.Join(',', releases)}]";

    [Fact]
    public void The_highest_release_is_taken_not_the_first()
    {
        var body = Feed(Release("v0.15.1-beta.1", true), Release("v0.17.0-beta.1", true), Release("v0.16.0-beta.2", true));

        ReleaseFeed.Latest(body, includePrerelease: true).Should().Be(SemVersion.Parse("0.17.0-beta.1"));
    }

    [Fact]
    public void Drafts_are_skipped()
    {
        var body = Feed(Release("v0.18.0", draft: true), Release("v0.16.0"));

        ReleaseFeed.Latest(body, includePrerelease: true).Should().Be(SemVersion.Parse("0.16.0"));
    }

    [Fact]
    public void Pre_releases_are_skipped_unless_asked_for()
    {
        var body = Feed(Release("v1.1.0-beta.1", prerelease: true), Release("v1.0.0"));

        ReleaseFeed.Latest(body, includePrerelease: false).Should().Be(Stable);
        ReleaseFeed.Latest(body, includePrerelease: true).Should().Be(SemVersion.Parse("1.1.0-beta.1"));
    }

    [Fact]
    public void A_tag_with_a_pre_release_counts_as_one_even_without_the_flag()
    {
        var body = Feed(Release("v1.1.0-beta.1", prerelease: false), Release("v1.0.0"));

        ReleaseFeed.Latest(body, includePrerelease: false).Should().Be(Stable);
    }

    [Theory]
    [InlineData("""[{"tag_name":"release-1","draft":false,"prerelease":false},{"tag_name":"v0.16.0","draft":false,"prerelease":false}]""")]
    [InlineData("""[{"tag_name":5,"draft":false,"prerelease":false},{"tag_name":"v0.16.0","draft":false,"prerelease":false}]""")]
    [InlineData("""[{"tag_name":"v9.0.0","draft":"no","prerelease":false},{"tag_name":"v0.16.0","draft":false,"prerelease":false}]""")]
    [InlineData("""[{"tag_name":"v9.0.0","draft":false},{"tag_name":"v0.16.0","draft":false,"prerelease":false}]""")]
    [InlineData("""[[],"x",null,{"tag_name":"v0.16.0","draft":false,"prerelease":false}]""")]
    public void A_release_that_is_not_well_formed_is_skipped(string body)
    {
        ReleaseFeed.Latest(body, includePrerelease: true).Should().Be(SemVersion.Parse("0.16.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("<html>captive portal</html>")]
    [InlineData("""[{"tag_name":"v1.0.0","draft":false,"prerelease":false}""")]
    public void Without_any_usable_release_there_is_none(string body)
    {
        ReleaseFeed.Latest(body, includePrerelease: true).Should().BeNull();
    }

    [Fact]
    public void Deep_nesting_is_refused()
    {
        var deep = new string('[', 200) + new string(']', 200);

        ReleaseFeed.Latest(deep, includePrerelease: true).Should().BeNull();
    }

    [Theory]
    [InlineData("v0.17.0-beta.1", UpdateStatus.UpdateAvailable)]
    [InlineData("v0.16.0-beta.1", UpdateStatus.UpToDate)]
    [InlineData("v0.15.1-beta.1", UpdateStatus.CurrentIsNewer)]
    public void A_good_answer_compares_the_latest_with_this_version(string tag, UpdateStatus status)
    {
        var outcome = UpdateCheckOutcome.FromResponse(200, null, false, Feed(Release(tag, true)), Beta);

        outcome.Status.Should().Be(status);
        outcome.Latest.Should().Be(SemVersion.Parse(tag[1..]));
        outcome.HttpStatus.Should().Be(200);
    }

    [Fact]
    public void A_stable_version_is_only_offered_a_stable_release()
    {
        var body = Feed(Release("v1.1.0-beta.1", true), Release("v1.0.0"));

        UpdateCheckOutcome.FromResponse(200, null, false, body, Stable).Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Fact]
    public void A_beta_version_is_offered_the_newer_stable_release()
    {
        var body = Feed(Release("v0.16.0-beta.2", true), Release("v0.16.0"));

        var outcome = UpdateCheckOutcome.FromResponse(200, null, false, body, Beta);

        outcome.Should().Be(new UpdateCheckOutcome(UpdateStatus.UpdateAvailable, 200, SemVersion.Parse("0.16.0")));
    }

    [Theory]
    [InlineData("<html>portal</html>")]
    [InlineData("[]")]
    public void A_success_code_without_a_release_list_is_an_invalid_answer(string body)
    {
        UpdateCheckOutcome.FromResponse(200, null, false, body, Beta).Status.Should().Be(UpdateStatus.InvalidResponse);
    }

    [Fact]
    public void Too_many_requests_is_rate_limited_with_the_wait()
    {
        var outcome = UpdateCheckOutcome.FromResponse(429, TimeSpan.FromMinutes(5), false, "", Beta);

        outcome.Should().Be(new UpdateCheckOutcome(UpdateStatus.RateLimited, 429, RetryAfter: TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void A_403_with_the_hourly_limit_used_up_is_rate_limited()
    {
        UpdateCheckOutcome.FromResponse(403, TimeSpan.FromMinutes(20), true, "{}", Beta).Status.Should().Be(UpdateStatus.RateLimited);
    }

    [Fact]
    public void A_403_with_requests_left_is_unavailable()
    {
        UpdateCheckOutcome.FromResponse(403, null, false, "{}", Beta).Status.Should().Be(UpdateStatus.Unavailable);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(-30, 1)]
    [InlineData(5 * 60 * 60, 60)]
    public void The_wait_is_one_minute_when_unknown_and_at_most_an_hour(int? seconds, int minutes)
    {
        TimeSpan? wait = seconds is { } s ? TimeSpan.FromSeconds(s) : null;

        UpdateCheckOutcome.FromResponse(429, wait, false, "", Beta).RetryAfter.Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(304)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public void Redirects_and_errors_are_unavailable(int status)
    {
        UpdateCheckOutcome.FromResponse(status, null, false, Feed(Release("v9.0.0")), Beta)
            .Should().Be(new UpdateCheckOutcome(UpdateStatus.Unavailable, status));
    }

    [Fact]
    public void A_proxy_that_wants_a_login_is_a_network_problem()
    {
        UpdateCheckOutcome.FromResponse(407, null, false, "", Beta).Status.Should().Be(UpdateStatus.Network);
    }

    [Theory]
    [InlineData(UpdateStatus.Network)]
    [InlineData(UpdateStatus.Timeout)]
    [InlineData(UpdateStatus.Cancelled)]
    public void Without_an_answer_only_the_status_is_kept(UpdateStatus status)
    {
        UpdateCheckOutcome.WithoutAnswer(status).Should().Be(new UpdateCheckOutcome(status));
    }

    [Fact]
    public void An_answer_status_cannot_be_made_without_an_answer()
    {
        var make = () => UpdateCheckOutcome.WithoutAnswer(UpdateStatus.UpToDate);

        make.Should().Throw<ArgumentOutOfRangeException>();
    }
}
