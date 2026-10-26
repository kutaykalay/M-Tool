using MTool.App.Hardware;
using MTool.App.Resources;
using MTool.App.ViewModels;
using MTool.Core.Ec;

namespace MTool.Tests.ViewModels;

/// <summary>
/// The words for what an experimental WMI2 write did (plan asama15 15c-5). Core hands over enums only; every
/// value gets a sentence, and the reasons the window cannot act on share one with the enum's name.
/// </summary>
public class Wmi2OutcomeTextTests
{
    private const string PreStatePath = @"C:\M-Tool\pre-mtool-state-wmi2.json";
    private static readonly RegisterWrite Write = new(0xD2, 0xC4);

    private static string Of(Wmi2WriteOutcome outcome) => Wmi2OutcomeText.Of(outcome, PreStatePath);

    private static string Internal(Enum reason) => string.Format(Strings.Wmi2_OutcomeInternal, reason);

    [Fact]
    public void An_applied_write_says_it_read_back()
    {
        Of(new Wmi2WriteOutcome(WriteStatus.Applied, 0xD2, 0xC1, Write)).Should().Be(Strings.Wmi2_OutcomeApplied);
    }

    [Fact]
    public void Applied_without_a_write_means_it_was_already_set()
    {
        Of(new Wmi2WriteOutcome(WriteStatus.Applied, 0xD2, 0xC4)).Should().Be(Strings.Wmi2_OutcomeAlreadySet);
    }

    [Fact]
    public void A_dry_run_says_nothing_was_written()
    {
        Of(new Wmi2WriteOutcome(WriteStatus.DryRun, 0xD2, 0xC1, Write)).Should().Be(Strings.Wmi2_OutcomeDryRun);
    }

    [Theory]
    [InlineData(Wmi2GatewayProblem.ReadBackDiffers, nameof(Strings.Wmi2_ProblemReadBack))]
    [InlineData(Wmi2GatewayProblem.WriteFailed, nameof(Strings.Wmi2_ProblemWriteFailed))]
    [InlineData(Wmi2GatewayProblem.RegisterMoved, nameof(Strings.Wmi2_ProblemRegisterMoved))]
    public void A_recovered_failure_says_why_and_that_the_old_value_is_back(Wmi2GatewayProblem problem, string why)
    {
        var text = Of(new Wmi2WriteOutcome(WriteStatus.FailedRecovered, 0xD2, 0xC1, Write, Problem: problem));

        text.Should().Be(string.Format(Strings.Wmi2_OutcomeRecovered, Text(why)));
    }

    [Fact]
    public void An_unrecovered_failure_says_why_and_what_to_do()
    {
        var text = Of(new Wmi2WriteOutcome(WriteStatus.FailedUnrecovered, 0xD2, 0xC1, Write, Problem: Wmi2GatewayProblem.ReadBackDiffers));

        text.Should().Be(string.Format(Strings.Wmi2_OutcomeUnrecovered, Strings.Wmi2_ProblemReadBack));
    }

    [Fact]
    public void A_failure_without_a_problem_still_says_the_write_failed()
    {
        var text = Of(new Wmi2WriteOutcome(WriteStatus.FailedUnrecovered, 0xD2, 0xC1, Write));

        text.Should().Be(string.Format(Strings.Wmi2_OutcomeUnrecovered, Strings.Wmi2_ProblemWriteFailed));
    }

    [Theory]
    [InlineData(Wmi2GatewayProblem.PreStateUnreadable, nameof(Strings.Wmi2_ProblemPreStateUnreadable))]
    [InlineData(Wmi2GatewayProblem.PreStateOtherFirmware, nameof(Strings.Wmi2_ProblemPreStateOtherFirmware))]
    public void A_snapshot_file_problem_names_the_file(Wmi2GatewayProblem problem, string key)
    {
        var text = Of(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, Block: Wmi2WriteBlock.NoPreState, Problem: problem));

        text.Should().Be(string.Format(Text(key), PreStatePath)).And.Contain(PreStatePath);
    }

    [Fact]
    public void A_missing_snapshot_without_a_problem_says_nothing_is_saved()
    {
        Of(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, Block: Wmi2WriteBlock.NoPreState)).Should().Be(Strings.Wmi2_RefusedNoPreState);
    }

    public static TheoryData<Wmi2Rejection, string?> Rejections => new()
    {
        { Wmi2Rejection.NotExactFirmware, nameof(Strings.Wmi2_ControlNotExact) },
        { Wmi2Rejection.ValueNotAllowed, nameof(Strings.Wmi2_RefusedValue) },
        { Wmi2Rejection.CurrentOutsideLayout, nameof(Strings.Wmi2_RefusedOutsideLayout) },
        { Wmi2Rejection.ListInvalid, nameof(Strings.Wmi2_RefusedListInvalid) },
        { Wmi2Rejection.NotWritable, null },
        { Wmi2Rejection.AdvancedFanMode, null },
        { Wmi2Rejection.OtherBitsChanged, null },
        { Wmi2Rejection.NotAWritePathTest, null },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void Every_rejection_has_its_sentence(Wmi2Rejection rejection, string? key)
    {
        Of(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, Rejection: rejection)).Should().Be(key is null ? Internal(rejection) : Text(key));
    }

    [Fact]
    public void Every_rejection_is_in_the_table() =>
        Rejections.Select(row => row.Data.Item1).Should().BeEquivalentTo(Enum.GetValues<Wmi2Rejection>());

    public static TheoryData<Wmi2WriteBlock, string?> Blocks => new()
    {
        { Wmi2WriteBlock.NotWmi2Only, nameof(Strings.Wmi2_RefusedNotWmi2Only) },
        { Wmi2WriteBlock.NotExactFirmware, nameof(Strings.Wmi2_ControlNotExact) },
        { Wmi2WriteBlock.LayoutNotVerified, nameof(Strings.Wmi2_RefusedOutsideLayout) },
        { Wmi2WriteBlock.NoPreState, nameof(Strings.Wmi2_RefusedNoPreState) },
        { Wmi2WriteBlock.SessionLocked, nameof(Strings.Wmi2_RefusedLocked) },
        { Wmi2WriteBlock.WritePathNotTested, nameof(Strings.Wmi2_RefusedWritePathTest) },
        { Wmi2WriteBlock.NoConsent, null },
        { Wmi2WriteBlock.UnknownIntent, null },
    };

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Every_block_has_its_sentence(Wmi2WriteBlock block, string? key)
    {
        Of(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, Block: block)).Should().Be(key is null ? Internal(block) : Text(key));
    }

    [Fact]
    public void Every_block_is_in_the_table() =>
        Blocks.Select(row => row.Data.Item1).Should().BeEquivalentTo(Enum.GetValues<Wmi2WriteBlock>());

    public static TheoryData<Wmi2GatewayProblem, string?> Problems => new()
    {
        { Wmi2GatewayProblem.PreStateNotTaken, nameof(Strings.Wmi2_ProblemPreStateNotTaken) },
        { Wmi2GatewayProblem.Unreachable, nameof(Strings.Wmi2_ProblemUnreachable) },
        { Wmi2GatewayProblem.ReadBackDiffers, nameof(Strings.Wmi2_ProblemReadBack) },
        { Wmi2GatewayProblem.WriteFailed, nameof(Strings.Wmi2_ProblemWriteFailed) },
        { Wmi2GatewayProblem.RegisterMoved, nameof(Strings.Wmi2_ProblemRegisterMoved) },
        { Wmi2GatewayProblem.Cancelled, nameof(Strings.Wmi2_ProblemCancelled) },
        { Wmi2GatewayProblem.WrongGateway, null },
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public void Every_refusing_problem_has_its_sentence(Wmi2GatewayProblem problem, string? key)
    {
        Of(new Wmi2WriteOutcome(WriteStatus.Rejected, 0xD2, Problem: problem)).Should().Be(key is null ? Internal(problem) : Text(key));
    }

    [Fact]
    public void Every_problem_has_a_sentence_somewhere() =>
        Problems.Select(row => row.Data.Item1)
            .Concat([Wmi2GatewayProblem.PreStateUnreadable, Wmi2GatewayProblem.PreStateOtherFirmware])
            .Should().BeEquivalentTo(Enum.GetValues<Wmi2GatewayProblem>());

    [Theory]
    [InlineData(nameof(Wmi2ControlUnavailable.NotWmi2), nameof(Strings.Wmi2_ControlNotWmi2))]
    [InlineData(nameof(Wmi2ControlUnavailable.FirmwareUnreadable), nameof(Strings.Wmi2_ControlFirmwareUnreadable))]
    [InlineData(nameof(Wmi2ControlUnavailable.OtherFirmware), nameof(Strings.Wmi2_ControlFirmwareChanged))]
    [InlineData(nameof(Wmi2ControlUnavailable.NotExactFirmware), nameof(Strings.Wmi2_ControlNotExact))]
    public void Every_reason_control_did_not_open_has_its_sentence(string reason, string key)
    {
        Wmi2OutcomeText.Of(Enum.Parse<Wmi2ControlUnavailable>(reason)).Should().Be(Text(key));
    }

    [Fact]
    public void Every_reason_control_did_not_open_is_covered() =>
        Enum.GetValues<Wmi2ControlUnavailable>().Select(Wmi2OutcomeText.Of).Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);

    [Theory]
    [InlineData(0x98, nameof(Strings.Wmi2_CoolerBoost))]
    [InlineData(0xD2, nameof(Strings.Wmi2_PerformanceMode))]
    [InlineData(0xD4, nameof(Strings.Wmi2_FanMode))]
    [InlineData(0xD7, nameof(Strings.Wmi2_ChargeLimit))]
    public void A_revert_line_names_what_was_written_back(byte register, string name)
    {
        var line = Wmi2OutcomeText.RevertLine(new Wmi2WriteOutcome(WriteStatus.Applied, register, 0x00), PreStatePath);

        line.Should().Be(string.Format(Strings.Wmi2_RevertLine, Text(name), Strings.Wmi2_OutcomeAlreadySet));
    }

    [Theory]
    [InlineData(WriteStatus.Applied, MessageKind.Info)]
    [InlineData(WriteStatus.DryRun, MessageKind.Info)]
    [InlineData(WriteStatus.Rejected, MessageKind.Warning)]
    [InlineData(WriteStatus.FailedRecovered, MessageKind.Warning)]
    [InlineData(WriteStatus.FailedUnrecovered, MessageKind.Warning)]
    public void Only_a_write_that_held_or_a_dry_run_is_information(WriteStatus status, MessageKind kind)
    {
        Wmi2OutcomeText.KindOf(new Wmi2WriteOutcome(status, 0xD2)).Should().Be(kind);
    }

    private static string Text(string key) => Strings.ResourceManager.GetString(key, Strings.Culture)!;
}
