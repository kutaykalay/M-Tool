using System.Runtime.InteropServices;
using MTool.App.Hardware;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Tests.Fakes;

namespace MTool.Tests.Hardware;

/// <summary>The decision part of the probe; the WMI query itself is tested by hand.</summary>
public class WmiProbeTests
{
    [Theory]
    [InlineData(true, false, WmiInterface.Wmi1)]
    [InlineData(true, true, WmiInterface.Wmi1)]
    [InlineData(false, true, WmiInterface.Wmi2)]
    public void Classifies_the_msi_interface(bool hasSoftware, bool hasAcpi, WmiInterface expected)
    {
        WmiProbe.Classify(hasSoftware, hasAcpi).Should().Be(expected);
    }

    [Fact]
    public void No_msi_class_means_no_interface()
    {
        WmiProbe.Classify(hasSoftware: false, hasAcpi: false).Should().BeNull();
    }

    [Fact]
    public void Wmi2_models_get_a_clear_unsupported_message()
    {
        WmiProbe.UnsupportedMessage(WmiInterface.Wmi2).Should().Contain("henüz desteklenmiyor").And.Contain("WMI2").And.Contain("salt okunur");
    }

    [Fact]
    public void The_log_text_is_English_whatever_the_language_of_the_window()
    {
        WmiProbe.UnsupportedLogMessage(WmiInterface.Wmi2).Should().Contain("not supported yet").And.Contain("WMI2");
        WmiProbe.UnsupportedLogMessage(null).Should().Contain("not supported yet").And.Contain("MSI WMI");
    }

    // --- the check before a session: only a definite answer stops start-up ---

    [Theory]
    [InlineData(WmiInterface.Wmi2)]
    [InlineData(null)]
    public void A_definite_answer_without_wmi1_stops_start_up(WmiInterface? detected)
    {
        var check = new WmiInterfaceCheck(_ => WmiProbeOutcome.Answered(detected));

        var act = () => check.EnsureWmi1(new ListLog());

        var thrown = act.Should().Throw<UnsupportedDeviceException>().WithMessage("*henüz*").Which;
        thrown.LogMessage.Should().Contain("not supported yet");
        thrown.Detected.Should().Be(detected, "the reports-only window shows the WMI2 values only when WMI2 was detected");
    }

    [Fact]
    public void A_probe_that_fails_lets_start_up_continue_as_before()
    {
        var log = new ListLog();
        var check = new WmiInterfaceCheck(_ => WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.Connect, null)));

        var act = () => check.EnsureWmi1(log);

        act.Should().NotThrow();
        log.Lines.Should().ContainSingle().Which.Should().StartWith("WARN").And.Contain("Connect timeout");
    }

    [Fact]
    public void A_confirmed_wmi1_is_not_probed_again()
    {
        var calls = 0;
        var check = new WmiInterfaceCheck(_ =>
        {
            calls++;
            return WmiProbeOutcome.Answered(WmiInterface.Wmi1);
        });

        check.EnsureWmi1(new ListLog());
        check.EnsureWmi1(new ListLog());

        calls.Should().Be(1);
    }

    [Fact]
    public void A_failed_probe_is_tried_again_next_time()
    {
        var calls = 0;
        var check = new WmiInterfaceCheck(_ => ++calls == 1
            ? WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.QuerySoftware, 0x8004100C))
            : WmiProbeOutcome.Answered(WmiInterface.Wmi1));

        check.EnsureWmi1(new ListLog());
        check.EnsureWmi1(new ListLog());

        calls.Should().Be(2);
    }

    [Fact]
    public void A_wmi1_found_by_probe_is_not_probed_again_by_the_session()
    {
        var budgets = new List<TimeSpan?>();
        var check = new WmiInterfaceCheck(budget =>
        {
            budgets.Add(budget);
            return WmiProbeOutcome.Answered(WmiInterface.Wmi1);
        });

        check.Probe(TimeSpan.FromSeconds(5));
        check.EnsureWmi1(new ListLog());

        budgets.Should().Equal(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(WmiInterface.Wmi2)]
    [InlineData(null)]
    public void Only_wmi1_is_remembered(WmiInterface? first)
    {
        var calls = 0;
        var check = new WmiInterfaceCheck(_ => ++calls == 1 ? WmiProbeOutcome.Answered(first) : WmiProbeOutcome.Answered(WmiInterface.Wmi1));

        check.Probe();
        check.Probe().Interface.Should().Be(WmiInterface.Wmi1);

        calls.Should().Be(2);
    }

    [Fact]
    public void Models_without_msi_wmi_get_a_clear_unsupported_message()
    {
        WmiProbe.UnsupportedMessage(null).Should().Contain("henüz desteklenmiyor").And.Contain("MSI WMI");
    }

    // --- the detailed probe: each step on its own, with its stage and code ---

    private const uint NotSupported = 0x8004100C;
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    private static COMException Com(uint code) => new("WMI says no", unchecked((int)code));

    /// <param name="budget">Generous unless the test is about a timeout: hung tests park pool threads.</param>
    private static WmiProbeOutcome Probe(Func<string, bool> hasClass, Func<object>? connect = null, TimeSpan? budget = null) =>
        WmiProbe.DetectDetailed(connect ?? (() => new object()), (_, className) => hasClass(className), budget ?? Generous);

    [Fact]
    public void A_found_wmi1_is_the_answer_without_asking_for_wmi2()
    {
        var asked = new List<string>();

        var outcome = Probe(className =>
        {
            asked.Add(className);
            return true;
        });

        outcome.Should().Be(WmiProbeOutcome.Answered(WmiInterface.Wmi1));
        asked.Should().Equal(["MSI_Software"], "WMI1 wins, so a hung MSI_ACPI query must not slow a WMI1 start-up");
    }

    [Fact]
    public void Wmi2_is_asked_only_after_wmi1_was_found_absent()
    {
        var asked = new List<string>();

        var outcome = Probe(className =>
        {
            asked.Add(className);
            return className == "MSI_ACPI";
        });

        outcome.Should().Be(WmiProbeOutcome.Answered(WmiInterface.Wmi2));
        asked.Should().Equal("MSI_Software", "MSI_ACPI");
    }

    [Fact]
    public void A_failed_software_query_does_not_ask_for_wmi2()
    {
        var asked = new List<string>();

        Probe(className =>
        {
            asked.Add(className);
            throw Com(NotSupported);
        });

        asked.Should().Equal(["MSI_Software"]);
    }

    [Fact]
    public void A_failed_connect_carries_its_code()
    {
        Probe(_ => true, connect: () => throw Com(0x80041003))
            .Should().Be(WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.Connect, 0x80041003)));
    }

    [Fact]
    public void A_failed_acpi_query_without_wmi1_carries_its_code()
    {
        Probe(className => className == "MSI_Software" ? false : throw Com(NotSupported))
            .Should().Be(WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.QueryAcpi, NotSupported)));
    }

    [Fact]
    public void A_failed_software_query_is_not_read_as_wmi2()
    {
        Probe(className => className == "MSI_ACPI" ? true : throw Com(NotSupported))
            .Should().Be(WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.QuerySoftware, NotSupported)));
    }

    [Fact]
    public void An_error_without_a_com_code_keeps_its_hresult()
    {
        var error = new InvalidOperationException("broken");

        Probe(_ => throw error).Failure.Should().Be(new WmiFailure(WmiProbeStage.QuerySoftware, unchecked((uint)error.HResult)));
    }

    [Fact]
    public void A_hung_query_is_a_timeout_at_its_stage()
    {
        using var hang = new ManualResetEventSlim();
        try
        {
            var outcome = Probe(className => className == "MSI_Software" ? false : hang.Wait(Timeout.Infinite), budget: Short);

            outcome.Should().Be(WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.QueryAcpi, null)));
        }
        finally
        {
            hang.Set();
        }
    }

    [Fact]
    public void The_steps_share_one_time_budget()
    {
        var budget = TimeSpan.FromSeconds(1);
        var slow = budget * 0.7;

        var outcome = Probe(
            _ =>
            {
                Thread.Sleep(slow);
                return true;
            },
            connect: () =>
            {
                Thread.Sleep(slow);
                return new object();
            },
            budget: budget);

        outcome.Failure.Should().Be(
            new WmiFailure(WmiProbeStage.QuerySoftware, null),
            "after a slow connect only part of the budget is left; a budget per step would have answered WMI1");
    }

    [Fact]
    public void Detect_turns_a_failure_into_a_wmi_error_naming_stage_and_code()
    {
        var act = () => WmiProbe.InterfaceOrThrow(WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.QuerySoftware, NotSupported)));

        act.Should().Throw<EcAccessException>().Which.ForLog().Should().Contain("QuerySoftware").And.Contain("0x8004100C");
    }

    [Fact]
    public void Detect_turns_a_timeout_into_a_no_answer_error()
    {
        var act = () => WmiProbe.InterfaceOrThrow(WmiProbeOutcome.Failed(new WmiFailure(WmiProbeStage.Connect, null)));

        act.Should().Throw<EcAccessException>().Which.ForLog().Should().Contain("Connect").And.Contain("timeout");
    }

    [Theory]
    [InlineData(WmiInterface.Wmi1)]
    [InlineData(WmiInterface.Wmi2)]
    [InlineData(null)]
    public void Detect_passes_an_answer_through(WmiInterface? detected)
    {
        WmiProbe.InterfaceOrThrow(WmiProbeOutcome.Answered(detected)).Should().Be(detected);
    }
}
