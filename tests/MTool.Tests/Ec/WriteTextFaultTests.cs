using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Core.Profiles;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>A resource lookup that fails must never change what the write path does: the lock, the recovery and the outcome stay.</summary>
public sealed class WriteTextFaultTests
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);

    private static Reset FailEveryLookup()
    {
        UserAndLogText.LookupFault.Value = key => throw new InvalidOperationException($"no resource {key}");
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => UserAndLogText.LookupFault.Value = null;
    }

    private static string WithoutLog(WriteRun run) => run.Facts[..run.Facts.IndexOf("\nlog:", StringComparison.Ordinal)];

    public static TheoryData<string> Names() =>
        ["applied", "dry run", "rejected", "failed and recovered", "failed, cooler boost last", "port disturbed"];

    private static async Task<WriteOutcome[]> Act(string name, WriteRig r)
    {
        switch (name)
        {
            case "dry run":
                return [await r.Gateway(Live with { DryRun = true }).ApplyAsync(WritePlans.Performance(PerformanceMode.Balanced))];
            case "rejected":
                return [await r.Gateway(Live with { FirmwareSupported = false }).ApplyAsync(WritePlans.ChargeLimit(79))];
            case "failed and recovered":
                r.Ec.StuckRegisters.Add(0x72);
                r.Ec[0x72] = 45;
                return [await r.Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"))];
            case "failed, cooler boost last":
                r.Ec.StuckRegisters.Add(0x72);
                r.Ec[0x72] = 99;
                return [await r.Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"))];
            case "port disturbed":
                r.Ec.AfterWrite = written => r.Ec[0xF2] = written == EcMap.ChargeLimit ? (byte)0xC4 : r.Ec[0xF2];
                return [await r.Gateway().ApplyAsync(WritePlans.ChargeLimit(79))];
            default:
                return [await r.Gateway().ApplyAsync(WritePlans.FanCurves(Presets.Cool.Curves, "Cool"))];
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task A_failing_text_lookup_changes_neither_the_outcome_nor_a_single_EC_access(string name)
    {
        var normal = await WriteRig.RunAsync("en", r => Act(name, r));

        using var _ = FailEveryLookup();
        var faulty = await WriteRig.RunAsync("en", r => Act(name, r));

        faulty.Statuses.Should().Equal(normal.Statuses);
        WithoutLog(faulty).Should().Be(WithoutLog(normal)); // status, planned, writes, reads, accesses, lock file, write enabled
        faulty.Texts.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_failed_write_still_locks_and_persists_the_lock_when_the_text_lookup_fails()
    {
        using var _ = FailEveryLookup();

        var run = await WriteRig.RunAsync("en", r => Act("failed and recovered", r));

        run.Statuses.Should().Equal("FailedRecovered");
        run.Facts.Should().Contain("lock file: True").And.Contain("write enabled: False");
        run.LockReason.Should().Contain("Write_LockAfterFailure");
    }

    [Fact]
    public async Task A_verified_write_is_still_reported_as_applied_when_the_text_lookup_fails()
    {
        using var _ = FailEveryLookup();

        var run = await WriteRig.RunAsync("tr", r => Act("applied", r));

        run.Statuses.Should().Equal("Applied");
    }

    [Fact]
    public void Say_gives_the_key_and_its_arguments_for_an_unknown_key()
    {
        var text = UserAndLogText.Say("No_Such_Key", 1, "two");

        text.Text.Should().Be("No_Such_Key: 1; two");
        text.Log.Should().Be(text.Text);
    }

    [Fact]
    public void Say_survives_a_format_that_does_not_fit_its_arguments()
    {
        var text = UserAndLogText.Say("Write_Locked"); // needs {0}

        text.Text.Should().Be("Write_Locked");
    }

    [Fact]
    public void Say_keeps_the_language_of_nested_texts_in_the_fallback()
    {
        var inner = new UserAndLogText("kullanıcı", "log");

        var text = UserAndLogText.Say("No_Such_Key", inner);

        text.Text.Should().Be("No_Such_Key: kullanıcı");
        text.Log.Should().Be("No_Such_Key: log");
    }

    [Fact]
    public void Say_survives_an_argument_that_throws_when_written()
    {
        var text = UserAndLogText.Say("No_Such_Key", new ThrowingToString());

        text.Text.Should().Be("No_Such_Key");
    }

    private sealed class ThrowingToString
    {
        public override string ToString() => throw new InvalidOperationException("boom");
    }
}
