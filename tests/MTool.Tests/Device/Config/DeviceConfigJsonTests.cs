using System.Text.Json;
using MTool.Core.Device.Config;
using MTool.Core.Profiles;

namespace MTool.Tests.Device.Config;

public class DeviceConfigJsonTests
{
    [Theory]
    [InlineData("\"0x6A\"", 0x6A)]
    [InlineData("\"0x6a\"", 0x6A)]
    [InlineData("\"0x0\"", 0x00)]
    [InlineData("\"0xFF\"", 0xFF)]
    public void Hex_byte_reads_0x_strings(string json, byte expected)
    {
        JsonSerializer.Deserialize<byte>(json, DeviceConfigLoader.Options).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"0xZZ\"")]
    [InlineData("\"256\"")]
    [InlineData("\"6A\"")]
    [InlineData("\"0x100\"")]
    [InlineData("\"0x\"")]
    [InlineData("\"\"")]
    [InlineData("\"0X6A\"")]
    [InlineData("\" 0x6A\"")]
    [InlineData("\"0x6A\\n\"")]
    [InlineData("\"0x6A\\r\\n\"")]
    [InlineData("106")]
    [InlineData("true")]
    [InlineData("null")]
    public void Hex_byte_rejects_anything_else(string json)
    {
        var act = () => JsonSerializer.Deserialize<byte>(json, DeviceConfigLoader.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Hex_byte_writes_two_upper_case_digits()
    {
        JsonSerializer.Serialize((byte)0x0A, DeviceConfigLoader.Options).Should().Be("\"0x0A\"");
    }

    [Fact]
    public void Curve_reads_up_speed_pairs()
    {
        var curve = JsonSerializer.Deserialize<FanCurve>("[[0, 45], [55, 50]]", DeviceConfigLoader.Options);

        curve!.Points.Should().Equal(new FanPoint(0, 45), new FanPoint(55, 50));
    }

    [Theory]
    [InlineData("[[0, 45, 1]]")]
    [InlineData("[[0]]")]
    [InlineData("[null]")]
    [InlineData("[[\"0\", 45]]")]
    [InlineData("{\"up\": 0}")]
    [InlineData("[[0, 45]")]
    public void Curve_rejects_anything_but_pairs_of_numbers(string json)
    {
        var act = () => JsonSerializer.Deserialize<FanCurve>(json, DeviceConfigLoader.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Curve_writes_up_speed_pairs()
    {
        JsonSerializer.Serialize(FanCurve.Of((0, 45), (55, 50)), DeviceConfigLoader.Options)
            .Should().Be("[[0,45],[55,50]]");
    }

    [Fact]
    public void Status_and_interface_are_camel_case_names()
    {
        JsonSerializer.Serialize(DeviceStatus.WriteVerified, DeviceConfigLoader.Options).Should().Be("\"writeVerified\"");
        JsonSerializer.Serialize(WmiInterface.Wmi1, DeviceConfigLoader.Options).Should().Be("\"wmi1\"");
    }

    [Fact]
    public void A_serialized_config_parses_back_to_the_same_values()
    {
        var json = JsonSerializer.Serialize(DeviceConfigFixtures.P65(), DeviceConfigLoader.Options);

        DeviceConfigLoader.Parse(json).Should().BeEquivalentTo(DeviceConfigFixtures.P65());
    }
}
