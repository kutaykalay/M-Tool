using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.Tests.Profiles;

public class ReapplySummaryTests
{
    private static WriteOutcome Outcome(WriteStatus status, string message) => new(status, [], message);

    [Fact]
    public void Worst_is_the_first_part_that_was_not_written()
    {
        var outcomes = new[]
        {
            Outcome(WriteStatus.Applied, "fan"),
            Outcome(WriteStatus.Rejected, "performance"),
            Outcome(WriteStatus.FailedRecovered, "later"),
        };

        ReapplySummary.Worst(outcomes)!.Message.Should().Be("performance");
    }

    [Theory]
    [InlineData(WriteStatus.Applied)]
    [InlineData(WriteStatus.DryRun)]
    public void Worst_is_the_last_part_when_every_part_was_written(WriteStatus status)
    {
        var outcomes = new[] { Outcome(status, "fan"), Outcome(status, "performance") };

        ReapplySummary.Worst(outcomes)!.Message.Should().Be("performance");
    }

    [Fact]
    public void Worst_of_nothing_is_null()
    {
        ReapplySummary.Worst([]).Should().BeNull();
    }
}
