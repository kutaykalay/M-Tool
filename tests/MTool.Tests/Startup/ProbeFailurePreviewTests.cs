#if DEBUG
using MTool.App.Startup;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Startup;

public class ProbeFailurePreviewTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("yes")]
    public void Anything_else_is_off(string? value) => ProbeFailurePreview.Parse(value).Should().BeNull();

    [Theory]
    [InlineData("1", false)]
    [InlineData(" noschema ", true)]
    public void One_or_noschema_turns_it_on(string value, bool schemaMissing) =>
        ProbeFailurePreview.Parse(value).Should().Be(new ProbeFailurePreview(schemaMissing));

    [Fact]
    public void The_probe_never_answers_and_the_session_never_opens()
    {
        var preview = new ProbeFailurePreview(SchemaMissing: false);

        preview.Probe(null).Failure.Should().Be(new WmiFailure(WmiProbeStage.QuerySoftware, 0x8004100C));
        preview.Probe(TimeSpan.FromSeconds(5)).Status.Should().Be(WmiProbeStatus.Failed);
        var open = () => preview.OpenSession<object>();
        open.Should().Throw<Exception>();
    }

    [Fact]
    public void Noschema_reports_the_registration_missing()
    {
        new ProbeFailurePreview(SchemaMissing: true).ReadSchema(() => throw new InvalidOperationException("not used"))
            .SchemaMissing.Should().BeTrue();
    }

    [Fact]
    public void Without_noschema_the_real_schema_is_read()
    {
        var real = new MsiSchemaInfo(MofImagePathState.Set, SchemaFileState.Exists, SchemaSignature.Unsigned);

        new ProbeFailurePreview(SchemaMissing: false).ReadSchema(() => real).Should().Be(real);
    }
}
#endif
