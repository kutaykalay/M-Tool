using MTool.Core.Ec;

namespace MTool.Tests.Ec;

/// <summary>
/// When a WMI2 experimental write may run at all (plan asama15 §4.4): every condition alone closes it,
/// and dry-run never opens what a condition closed.
/// </summary>
public class Wmi2WritePolicyTests
{
    private static readonly Wmi2WritePolicy Open = new(
        Wmi2Only: true,
        FirmwareExact: true,
        Consented: true,
        LayoutVerified: true,
        PreStateSaved: true,
        SessionLockReason: null,
        WritePathTested: true,
        DryRun: false);

    public static TheoryData<Wmi2WritePolicy, Wmi2WriteBlock> EachConditionAlone => new()
    {
        { Open with { Wmi2Only = false }, Wmi2WriteBlock.NotWmi2Only },
        { Open with { FirmwareExact = false }, Wmi2WriteBlock.NotExactFirmware },
        { Open with { Consented = false }, Wmi2WriteBlock.NoConsent },
        { Open with { LayoutVerified = false }, Wmi2WriteBlock.LayoutNotVerified },
        { Open with { PreStateSaved = false }, Wmi2WriteBlock.NoPreState },
        { Open with { SessionLockReason = "write to 0xD2 failed" }, Wmi2WriteBlock.SessionLocked },
    };

    [Fact]
    public void A_change_runs_when_every_condition_holds()
    {
        Open.Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.Write);
        Open.Decide(Wmi2WriteIntent.WritePathTest).Should().Be(Wmi2WriteVerdict.Write);
    }

    [Theory]
    [MemberData(nameof(EachConditionAlone))]
    public void Each_condition_alone_refuses_a_change(Wmi2WritePolicy policy, Wmi2WriteBlock block)
    {
        policy.Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.Refused(block));
    }

    [Theory]
    [MemberData(nameof(EachConditionAlone))]
    public void Each_condition_alone_refuses_the_write_path_test(Wmi2WritePolicy policy, Wmi2WriteBlock block)
    {
        policy.Decide(Wmi2WriteIntent.WritePathTest).Should().Be(Wmi2WriteVerdict.Refused(block));
    }

    [Theory]
    [MemberData(nameof(EachConditionAlone))]
    public void Dry_run_does_not_open_what_a_condition_closed(Wmi2WritePolicy policy, Wmi2WriteBlock block)
    {
        (policy with { DryRun = true }).Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.Refused(block));
    }

    [Fact]
    public void The_first_real_change_waits_for_the_write_path_test()
    {
        var untested = Open with { WritePathTested = false };

        untested.Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.Refused(Wmi2WriteBlock.WritePathNotTested));
        untested.Decide(Wmi2WriteIntent.WritePathTest).Should().Be(Wmi2WriteVerdict.Write);
    }

    [Theory]
    [InlineData(Wmi2WriteIntent.Change)]
    [InlineData(Wmi2WriteIntent.WritePathTest)]
    public void Dry_run_plans_and_writes_nothing(Wmi2WriteIntent intent)
    {
        (Open with { DryRun = true }).Decide(intent).Should().Be(Wmi2WriteVerdict.DryRun);
    }

    [Fact]
    public void Dry_run_shows_a_change_before_the_write_path_test_because_nothing_is_written()
    {
        (Open with { DryRun = true, WritePathTested = false }).Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.DryRun);
    }

    [Fact]
    public void An_undefined_intent_is_refused()
    {
        Open.Decide((Wmi2WriteIntent)7).Should().Be(Wmi2WriteVerdict.Refused(Wmi2WriteBlock.UnknownIntent));
    }

    [Fact]
    public void An_empty_session_lock_reason_still_locks()
    {
        (Open with { SessionLockReason = "" }).Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.Refused(Wmi2WriteBlock.SessionLocked));
    }

    [Fact]
    public void The_most_basic_condition_is_named_when_several_fail()
    {
        var closed = new Wmi2WritePolicy(
            Wmi2Only: false,
            FirmwareExact: false,
            Consented: false,
            LayoutVerified: false,
            PreStateSaved: false,
            SessionLockReason: "locked",
            WritePathTested: false,
            DryRun: true);

        closed.Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.Refused(Wmi2WriteBlock.NotWmi2Only));
        (closed with { Wmi2Only = true }).Decide(Wmi2WriteIntent.Change).Should().Be(Wmi2WriteVerdict.Refused(Wmi2WriteBlock.NotExactFirmware));
    }

    [Fact]
    public void A_closed_condition_is_named_before_an_undefined_intent()
    {
        (Open with { Consented = false }).Decide((Wmi2WriteIntent)7).Should().Be(Wmi2WriteVerdict.Refused(Wmi2WriteBlock.NoConsent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_every_condition_holding_opens_a_write(bool dryRun)
    {
        const int Conditions = 7;
        const int AllHold = (1 << Conditions) - 1;
        for (var bits = 0; bits <= AllHold; bits++)
        {
            bool On(int i) => (bits & (1 << i)) != 0;
            var policy = new Wmi2WritePolicy(
                Wmi2Only: On(0),
                FirmwareExact: On(1),
                Consented: On(2),
                LayoutVerified: On(3),
                PreStateSaved: On(4),
                SessionLockReason: On(5) ? null : "locked",
                WritePathTested: On(6),
                DryRun: dryRun);

            var verdict = policy.Decide(Wmi2WriteIntent.Change);

            // Dry-run writes nothing, so it does not wait for the write-path test (condition 6).
            var holding = dryRun ? (bits | (1 << 6)) == AllHold : bits == AllHold;
            var expected = !holding ? Wmi2WriteVerdictKind.Refused : dryRun ? Wmi2WriteVerdictKind.DryRun : Wmi2WriteVerdictKind.Write;
            verdict.Kind.Should().Be(expected, $"conditions 0b{Convert.ToString(bits, 2)}");
            (verdict.Kind == Wmi2WriteVerdictKind.Refused).Should().Be(verdict.Block is not null);
        }
    }
}
