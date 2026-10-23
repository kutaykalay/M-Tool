using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Diagnostics;

public sealed class WmiProbeOutcomeTests
{
    private static readonly WmiFailure NotSupported = new(WmiProbeStage.QuerySoftware, 0x8004100C);
    private static readonly WmiFailure AcpiTimeout = new(WmiProbeStage.QueryAcpi, null);

    [Theory]
    [InlineData(true, false, WmiInterface.Wmi1)]
    [InlineData(true, true, WmiInterface.Wmi1)]
    [InlineData(false, true, WmiInterface.Wmi2)]
    public void Two_answers_give_the_interface(bool hasSoftware, bool hasAcpi, WmiInterface expected)
    {
        var outcome = WmiProbeOutcome.Decide(WmiClassAnswer.Of(hasSoftware), WmiClassAnswer.Of(hasAcpi));

        outcome.Should().Be(WmiProbeOutcome.Answered(expected));
        outcome.Status.Should().Be(WmiProbeStatus.Ok);
    }

    [Fact]
    public void No_class_is_an_answer_not_a_failure()
    {
        var outcome = WmiProbeOutcome.Decide(WmiClassAnswer.Of(false), WmiClassAnswer.Of(false));

        outcome.Interface.Should().BeNull();
        outcome.Failure.Should().BeNull("\"no MSI class\" and \"WMI did not answer\" must stay apart");
        outcome.Status.Should().Be(WmiProbeStatus.Ok);
    }

    [Fact]
    public void A_failed_acpi_query_does_not_lose_a_found_wmi1()
    {
        WmiProbeOutcome.Decide(WmiClassAnswer.Of(true), WmiClassAnswer.Failed(AcpiTimeout))
            .Should().Be(WmiProbeOutcome.Answered(WmiInterface.Wmi1));
    }

    [Fact]
    public void A_failed_software_query_never_falls_to_wmi2()
    {
        var outcome = WmiProbeOutcome.Decide(WmiClassAnswer.Failed(NotSupported), WmiClassAnswer.Of(true));

        outcome.Interface.Should().BeNull("WMI1 wins when both exist, so WMI2 cannot be claimed while WMI1 is unknown");
        outcome.Failure.Should().Be(NotSupported);
        outcome.Status.Should().Be(WmiProbeStatus.Failed);
    }

    [Fact]
    public void A_failed_acpi_query_without_wmi1_is_a_failure()
    {
        var outcome = WmiProbeOutcome.Decide(WmiClassAnswer.Of(false), WmiClassAnswer.Failed(AcpiTimeout));

        outcome.Should().Be(WmiProbeOutcome.Failed(AcpiTimeout));
        outcome.Status.Should().Be(WmiProbeStatus.Timeout);
    }

    [Fact]
    public void The_software_failure_is_kept_when_both_queries_fail()
    {
        WmiProbeOutcome.Decide(WmiClassAnswer.Failed(NotSupported), WmiClassAnswer.Failed(AcpiTimeout))
            .Failure.Should().Be(NotSupported);
    }

    [Fact]
    public void A_code_is_a_number_shown_as_eight_hex_digits()
    {
        NotSupported.CodeText.Should().Be("0x8004100C");
        new WmiFailure(WmiProbeStage.Connect, 0x5).CodeText.Should().Be("0x00000005");
    }

    [Fact]
    public void A_failure_without_a_code_is_a_timeout()
    {
        AcpiTimeout.TimedOut.Should().BeTrue();
        AcpiTimeout.CodeText.Should().Be("timeout");
        NotSupported.TimedOut.Should().BeFalse();
    }

    [Fact]
    public void A_failure_reads_as_stage_and_code()
    {
        NotSupported.ToString().Should().Be("QuerySoftware 0x8004100C");
        AcpiTimeout.ToString().Should().Be("QueryAcpi timeout");
    }

    [Fact]
    public void A_class_answer_is_either_found_or_failed()
    {
        WmiClassAnswer.Of(true).Should().Be(new WmiClassAnswer(true, null));
        WmiClassAnswer.Failed(NotSupported).Should().Be(new WmiClassAnswer(false, NotSupported));
    }
}
