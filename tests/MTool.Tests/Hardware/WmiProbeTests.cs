using MTool.App.Hardware;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
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
        WmiProbe.UnsupportedMessage(WmiInterface.Wmi2).Should().Contain("henüz desteklenmiyor").And.Contain("WMI2");
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
        var check = new WmiInterfaceCheck(() => detected);

        var act = () => check.EnsureWmi1(new ListLog());

        act.Should().Throw<UnsupportedDeviceException>().WithMessage("*henüz desteklenmiyor*").Which.LogMessage.Should().Contain("not supported yet");
    }

    [Fact]
    public void A_probe_that_fails_lets_start_up_continue_as_before()
    {
        var log = new ListLog();
        var check = new WmiInterfaceCheck(() => throw new EcAccessException("WMI cold, timed out"));

        var act = () => check.EnsureWmi1(log);

        act.Should().NotThrow();
        log.Lines.Should().ContainSingle().Which.Should().StartWith("WARN").And.Contain("WMI cold");
    }

    [Fact]
    public void A_confirmed_wmi1_is_not_probed_again()
    {
        var calls = 0;
        var check = new WmiInterfaceCheck(() =>
        {
            calls++;
            return WmiInterface.Wmi1;
        });

        check.EnsureWmi1(new ListLog());
        check.EnsureWmi1(new ListLog());

        calls.Should().Be(1);
    }

    [Fact]
    public void A_failed_probe_is_tried_again_next_time()
    {
        var calls = 0;
        var check = new WmiInterfaceCheck(() => ++calls == 1 ? throw new EcAccessException("cold") : WmiInterface.Wmi1);

        check.EnsureWmi1(new ListLog());
        check.EnsureWmi1(new ListLog());

        calls.Should().Be(2);
    }

    [Fact]
    public void Models_without_msi_wmi_get_a_clear_unsupported_message()
    {
        WmiProbe.UnsupportedMessage(null).Should().Contain("henüz desteklenmiyor").And.Contain("MSI WMI");
    }
}
