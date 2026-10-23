using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

/// <summary>The words the server accepts; a new enum member must get its own word, not fall into another's.</summary>
public sealed class DiagnosticWordsTests
{
    [Fact]
    public void Every_probe_status_and_stage_has_its_own_word()
    {
        Enum.GetValues<WmiProbeStatus>().Select(DiagnosticWords.Status).Should().Equal("ok", "failed", "timeout");
        Enum.GetValues<WmiProbeStage>().Select(DiagnosticWords.Stage).Should().Equal("connect", "query_software", "query_acpi");
    }

    [Fact]
    public void Every_schema_state_has_its_own_word()
    {
        Enum.GetValues<MofImagePathState>().Select(DiagnosticWords.Registration).Should().Equal("set", "not_set", "unreadable");
        Enum.GetValues<SchemaFileState>().Select(DiagnosticWords.File).Should().Equal("exists", "missing", "unknown");
        Enum.GetValues<SchemaSignature>().Select(DiagnosticWords.Signature).Should().Equal("micro_star", "unsigned", "other", "unknown");
    }

    [Fact]
    public void A_value_outside_the_enum_has_no_word()
    {
        var act = () => DiagnosticWords.Stage((WmiProbeStage)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
