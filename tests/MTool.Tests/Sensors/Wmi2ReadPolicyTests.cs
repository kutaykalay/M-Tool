using MTool.Core.Sensors;

namespace MTool.Tests.Sensors;

/// <summary>WMI2 is read at most every 2 seconds (CLAUDE.md, "WMI2 canlı okuma").</summary>
public class Wmi2ReadPolicyTests
{
    [Theory]
    [InlineData(1000, 2000)]
    [InlineData(1999, 2000)]
    [InlineData(2000, 2000)]
    [InlineData(5000, 5000)]
    [InlineData(0, 2000)]
    [InlineData(-1000, 2000)]
    [InlineData(-1, 2000)] // Timeout.InfiniteTimeSpan
    public void An_interval_below_two_seconds_becomes_two_seconds(int requestedMs, int expectedMs)
    {
        Wmi2ReadPolicy.Interval(TimeSpan.FromMilliseconds(requestedMs)).Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void The_minimum_is_two_seconds()
    {
        Wmi2ReadPolicy.MinInterval.Should().Be(TimeSpan.FromSeconds(2));
    }
}
