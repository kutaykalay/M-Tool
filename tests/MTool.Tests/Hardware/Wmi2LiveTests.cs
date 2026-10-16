using MTool.App.Hardware;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Hardware;

/// <summary>The WMI2 live session opens only on a laptop whose probe answered WMI2 (CLAUDE.md, "WMI2 canlı okuma").</summary>
public class Wmi2LiveTests
{
    [Theory]
    [InlineData(WmiInterface.Wmi1)]
    [InlineData(null)]
    public void Without_wmi2_nothing_is_opened(WmiInterface? detected)
    {
        var opened = 0;
        var log = new ListLog();

        var session = Wmi2Live.OpenIfWmi2<object>(() => detected, () => ++opened, log);

        session.Should().BeNull();
        opened.Should().Be(0);
        log.Lines.Should().BeEmpty("a WMI1 laptop is the normal case, not worth a line");
    }

    [Fact]
    public void A_failure_to_open_reaches_the_caller()
    {
        var act = () => Wmi2Live.OpenIfWmi2<object>(() => WmiInterface.Wmi2, () => throw new EcAccessException("no instance"), new ListLog());

        act.Should().Throw<EcAccessException>().WithMessage("no instance");
    }

    [Fact]
    public void A_probe_that_fails_opens_nothing_and_says_why_in_the_log()
    {
        var opened = 0;
        var log = new ListLog();

        var session = Wmi2Live.OpenIfWmi2<object>(() => throw new EcAccessException("WMI cold"), () => ++opened, log);

        session.Should().BeNull();
        opened.Should().Be(0);
        log.Lines.Should().ContainSingle().Which.Should().StartWith("WARN").And.Contain("WMI cold");
    }

    [Fact]
    public void With_wmi2_the_session_is_opened_once()
    {
        var opened = 0;

        var session = Wmi2Live.OpenIfWmi2<object>(() => WmiInterface.Wmi2, () => ++opened, new ListLog());

        session.Should().Be(1);
        opened.Should().Be(1);
    }
}
