using MTool.Core.Device;
using MTool.Core.Profiles;

namespace MTool.Tests.Device;

public class RpmCodecTests
{
    [Theory]
    [InlineData(0, 160, 2987)]
    [InlineData(0, 157, 3044)]
    [InlineData(0x01, 0x00, 1867)]
    public void Converts_big_endian_period_to_rpm(byte high, byte low, int expectedRpm)
    {
        RpmCodec.ToRpm(high, low).Should().Be(expectedRpm);
    }

    [Fact]
    public void Stopped_fan_reads_zero()
    {
        RpmCodec.ToRpm(0, 0).Should().Be(0);
    }
}

public class FanTableCodecTests
{
    // CPU table read from the P65 in Faz 0 (factory Default).
    private static readonly byte[] CpuUp = [55, 64, 70, 76, 82, 88];
    private static readonly byte[] CpuSpeeds = [45, 50, 60, 70, 75, 80, 80];

    [Fact]
    public void Decodes_seven_points_from_up_thresholds_and_speeds()
    {
        var curve = FanTableCodec.Decode(CpuUp, CpuSpeeds);

        curve.Points.Should().Equal(
            new FanPoint(0, 45),
            new FanPoint(55, 50),
            new FanPoint(64, 60),
            new FanPoint(70, 70),
            new FanPoint(76, 75),
            new FanPoint(82, 80),
            new FanPoint(88, 80));
    }

    [Theory]
    [InlineData(5, 7)]
    [InlineData(6, 6)]
    public void Rejects_tables_with_wrong_lengths(int upCount, int speedCount)
    {
        var act = () => FanTableCodec.Decode(new byte[upCount], new byte[speedCount]);

        act.Should().Throw<ArgumentException>();
    }
}
