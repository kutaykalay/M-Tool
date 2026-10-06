using System.Text.Json;
using System.Text.Json.Nodes;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device.Config;

public class DeviceConfigLoaderTests
{
    private readonly ListLog _log = new();

    [Fact]
    public void Loads_a_valid_record()
    {
        var configs = DeviceConfigLoader.Load([("good.json", P65Json())], _log);

        configs.Should().ContainSingle().Which.Id.Should().Be("msi-p65-creator-9se");
        _log.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Skips_and_logs_unreadable_json(string json)
    {
        var configs = DeviceConfigLoader.Load([("bad.json", json), ("good.json", P65Json())], _log);

        configs.Select(c => c.Id).Should().Equal("msi-p65-creator-9se");
        _log.Lines.Should().ContainSingle().Which.Should().Contain("bad.json");
    }

    [Fact]
    public void Skips_and_logs_a_record_that_fails_validation()
    {
        var invalid = Edit(P65Json(), n => n["schemaVersion"] = 2);

        var configs = DeviceConfigLoader.Load([("v2.json", invalid)], _log);

        configs.Should().BeEmpty();
        _log.Lines.Should().ContainSingle().Which.Should().Contain("v2.json").And.Contain("schemaVersion");
    }

    [Theory]
    [InlineData("unknown field")]
    [InlineData("missing field")]
    [InlineData("null id")]
    [InlineData("numeric enum")]
    [InlineData("number as string")]
    [InlineData("bad hex")]
    [InlineData("comment")]
    [InlineData("hex with line break")]
    [InlineData("duplicate property")]
    [InlineData("duplicate preset key")]
    public void Rejects_loose_json(string kind)
    {
        var json = kind switch
        {
            "unknown field" => Edit(P65Json(), n => n["portRegistres"] = new JsonArray()),
            "missing field" => Edit(P65Json(), n => n.AsObject().Remove("fans")),
            "null id" => Edit(P65Json(), n => n["id"] = null),
            "numeric enum" => Edit(P65Json(), n => n["status"] = 2),
            "number as string" => Edit(P65Json(), n => n["schemaVersion"] = "1"),
            "bad hex" => Edit(P65Json(), n => n["portRegisters"] = new JsonArray("0x98", "0xZZ")),
            "hex with line break" => Edit(P65Json(), n => n["portRegisters"] = new JsonArray("0x98", "0xEF\n")),
            "duplicate property" => "{\"id\":\"x\"," + P65Json()[1..],
            "duplicate preset key" => P65Json().Replace("\"curves\":{", "\"curves\":{\"cpu\":[[0,50]],", StringComparison.Ordinal),
            _ => "// note\n" + P65Json(),
        };

        DeviceConfigLoader.Load([("loose.json", json)], _log).Should().BeEmpty();
        _log.Lines.Should().ContainSingle().Which.Should().Contain("loose.json");
    }

    [Fact]
    public void Drops_and_logs_both_records_that_claim_the_same_firmware()
    {
        var copy = Edit(P65Json(), n => n["id"] = "msi-p65-copy");
        var draft = JsonSerializer.Serialize(DeviceConfigFixtures.Draft(), DeviceConfigLoader.Options);

        var configs = DeviceConfigLoader.Load([("a.json", P65Json()), ("b.json", copy), ("c.json", draft)], _log);

        configs.Select(c => c.Id).Should().Equal("draft-test");
        _log.Lines.Should().HaveCount(2).And.AllSatisfy(l => l.Should().Contain("16Q4EMS2.107"));
    }

    [Fact]
    public void No_sources_give_an_empty_catalog()
    {
        DeviceConfigLoader.Load([], _log).Should().BeEmpty();
        _log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Embedded_records_load_without_warnings()
    {
        var configs = DeviceConfigLoader.LoadEmbedded(_log);

        configs.Select(c => c.Id).Should().Equal("msi-p65-creator-9se");
        _log.Lines.Should().BeEmpty();
    }

    private static string P65Json() => JsonSerializer.Serialize(DeviceConfigFixtures.P65(), DeviceConfigLoader.Options);

    private static string Edit(string json, Action<JsonNode> change)
    {
        var node = JsonNode.Parse(json)!;
        change(node);
        return node.ToJsonString();
    }
}
