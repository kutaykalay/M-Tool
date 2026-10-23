using MTool.App.Hardware;
using MTool.App.Startup;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Startup;

/// <summary>Start-up's choice between the session, the reports-only window and the error box, without WMI.</summary>
public sealed class StartupRouteTests
{
    private static readonly WmiProbeOutcome Wmi1 = WmiProbeOutcome.Answered(WmiInterface.Wmi1);
    private static readonly WmiProbeOutcome Wmi2 = WmiProbeOutcome.Answered(WmiInterface.Wmi2);
    private static readonly WmiProbeOutcome NoClass = WmiProbeOutcome.Answered(null);
    private static readonly WmiProbeOutcome NotSupported = WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.QuerySoftware, 0x8004100C));
    private static readonly MsiSchemaInfo Registered = new(MofImagePathState.Set, SchemaFileState.Exists, SchemaSignature.Unsigned);
    private static readonly MsiSchemaInfo NotRegistered = new(MofImagePathState.NotSet, SchemaFileState.Unknown, SchemaSignature.Unknown);

    private readonly ListLog _log = new();
    private readonly List<TimeSpan?> _budgets = [];
    private readonly List<TimeSpan> _waits = [];
    private int _opens;

    private StartupDecision<object> Decide(
        IReadOnlyList<WmiProbeOutcome> probes, Func<int, object>? open = null, MsiSchemaInfo? schema = null)
    {
        return StartupRoute.Decide(
            budget =>
            {
                _budgets.Add(budget);
                return probes[Math.Min(_budgets.Count, probes.Count) - 1];
            },
            () => (open ?? (_ => new object()))(++_opens),
            _waits.Add,
            () => schema ?? Registered,
            _log);
    }

    private static Exception Unreachable() => new EcAccessException("WMI call (connect) failed: incompatible");

    [Fact]
    public void A_wmi1_answer_opens_the_session_without_waiting()
    {
        var decision = Decide([Wmi1]);

        decision.Session.Should().NotBeNull();
        _opens.Should().Be(1);
        _waits.Should().BeEmpty();
        _budgets.Should().ContainSingle();
    }

    [Fact]
    public void A_confirmed_wmi1_that_cannot_open_is_the_error_box()
    {
        var decision = Decide([Wmi1], open: _ => throw Unreachable());

        decision.SessionError.Should().BeOfType<EcAccessException>();
        decision.ReportsOnly.Should().BeNull();
        _opens.Should().Be(1, "a verified WMI1 that fails is a real problem, not a reason to retry");
    }

    [Theory]
    [InlineData(WmiInterface.Wmi2)]
    [InlineData(null)]
    public void A_definite_answer_without_wmi1_is_the_reports_only_window(WmiInterface? detected)
    {
        var decision = Decide([WmiProbeOutcome.Answered(detected)]);

        decision.ReportsOnly!.Detected.Should().Be(detected);
        decision.ReportsOnly.Message.Should().Contain("henüz desteklenmiyor");
        _opens.Should().Be(0);
    }

    [Fact]
    public void A_failed_probe_still_opens_the_session_as_before()
    {
        var decision = Decide([NotSupported, Wmi1]);

        decision.Session.Should().NotBeNull("a cold WMI at log-on must not cost the --tray start");
        _waits.Should().BeEmpty();
        _log.Lines.Should().Contain(l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains("QuerySoftware 0x8004100C"));
    }

    [Fact]
    public void When_neither_answers_the_probe_is_tried_again_once_after_a_pause()
    {
        Decide([NotSupported, NotSupported], open: _ => throw Unreachable());

        _waits.Should().Equal(StartupRoute.RetryDelay);
        _budgets.Should().Equal(null, StartupRoute.RetryBudget);
    }

    [Fact]
    public void Still_no_answer_is_the_reports_only_window_with_the_step_and_code_not_an_error_box()
    {
        var decision = Decide([NotSupported, NotSupported], open: _ => throw Unreachable());

        decision.SessionError.Should().BeNull();
        decision.ReportsOnly!.Detected.Should().BeNull("WMI2 values are offered only when WMI2 was detected");
        decision.ReportsOnly.Message.Should().Contain("QuerySoftware 0x8004100C");
        decision.ReportsOnly.LogMessage.Should().Contain("did not answer").And.Contain("QuerySoftware 0x8004100C");
    }

    [Fact]
    public void Wmi2_on_the_second_probe_is_the_reports_only_window_with_live_values()
    {
        var decision = Decide([NotSupported, Wmi2], open: _ => throw Unreachable());

        decision.ReportsOnly!.Detected.Should().Be(WmiInterface.Wmi2);
        _opens.Should().Be(1);
    }

    [Fact]
    public void Wmi1_on_the_second_probe_opens_the_session_once_more()
    {
        var decision = Decide([NotSupported, Wmi1], open: n => n == 1 ? throw Unreachable() : new object());

        decision.Session.Should().NotBeNull();
        _opens.Should().Be(2);
    }

    [Fact]
    public void Wmi1_on_the_second_probe_that_still_cannot_open_is_the_error_box()
    {
        var decision = Decide([NotSupported, Wmi1], open: _ => throw Unreachable());

        decision.SessionError.Should().NotBeNull();
        _opens.Should().Be(2);
    }

    [Fact]
    public void A_session_that_finds_no_wmi1_itself_is_the_reports_only_window()
    {
        var unsupported = new UnsupportedDeviceException("henüz desteklenmiyor", "not supported yet", WmiInterface.Wmi2);

        var decision = Decide([NotSupported], open: _ => throw unsupported);

        decision.ReportsOnly.Should().Be(new ReportsOnlyRoute("henüz desteklenmiyor", "not supported yet", WmiInterface.Wmi2));
        _waits.Should().BeEmpty();
    }

    [Fact]
    public void Without_the_msi_schema_the_message_says_to_install_msi_center_once()
    {
        var decision = Decide([NoClass], schema: NotRegistered);

        decision.ReportsOnly!.Message.Should().Contain("MSI Center'ı bu dizüstüne bir kez kurun");
        decision.ReportsOnly.LogMessage.Should().Contain("Hint: Install MSI Center on this laptop once");
    }

    [Fact]
    public void A_probe_that_never_answers_without_the_schema_gets_the_hint_too()
    {
        Decide([NotSupported, NotSupported], open: _ => throw Unreachable(), schema: NotRegistered)
            .ReportsOnly!.Message.Should().Contain("bir kez kurun");
    }

    [Fact]
    public void With_the_schema_registered_there_is_no_hint()
    {
        Decide([NoClass]).ReportsOnly!.Message.Should().NotContain("MSI Center");
    }

    [Fact]
    public void A_working_wmi2_gets_no_hint_even_without_the_registration()
    {
        Decide([Wmi2], schema: NotRegistered).ReportsOnly!.Message.Should().NotContain("bir kez kurun");
    }

    [Fact]
    public void A_schema_read_that_throws_only_loses_the_hint()
    {
        var decision = StartupRoute.Decide<object>(
            _ => NoClass, () => new object(), _ => { }, () => throw new InvalidOperationException("kayıt defteri"), _log);

        decision.ReportsOnly!.Message.Should().NotContain("bir kez kurun");
    }
}
