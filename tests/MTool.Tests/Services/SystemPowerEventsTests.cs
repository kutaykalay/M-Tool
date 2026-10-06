using System.Windows.Forms;
using Microsoft.Win32;
using MTool.App.Services;
using MTool.Core.Power;
using MTool.Tests.Fakes;

namespace MTool.Tests.Services;

public class SystemPowerEventsTests
{
    private readonly ListLog _log = new();
    private PowerLineStatus _status = PowerLineStatus.Online;
    private int _changes;

    [Theory]
    [InlineData(PowerLineStatus.Online, PowerSource.Ac)]
    [InlineData(PowerLineStatus.Offline, PowerSource.Battery)]
    [InlineData(PowerLineStatus.Unknown, null)]
    [InlineData((PowerLineStatus)42, null)]
    public void Windows_power_line_status_maps_to_a_source_or_unknown(PowerLineStatus status, PowerSource? expected) =>
        SystemPowerEvents.From(status).Should().Be(expected);

    [Fact]
    public void The_start_source_is_logged_once()
    {
        using var events = Create();

        _log.Lines.Should().Equal("INFO Güç: kaynak Ac");
    }

    [Fact]
    public void A_status_change_on_the_same_source_is_neither_raised_nor_logged()
    {
        using var events = Create();

        Raise(events, PowerModes.StatusChange);
        Raise(events, PowerModes.StatusChange);

        _changes.Should().Be(0);
        _log.Lines.Should().Equal("INFO Güç: kaynak Ac");
    }

    [Fact]
    public void A_source_change_is_raised_once_however_many_status_changes_follow()
    {
        using var events = Create();

        _status = PowerLineStatus.Offline;
        Raise(events, PowerModes.StatusChange);
        Raise(events, PowerModes.StatusChange);
        Raise(events, PowerModes.StatusChange);

        _changes.Should().Be(1);
        _log.Lines.Should().Equal("INFO Güç: kaynak Ac", "INFO Güç: kaynak Ac→Battery");
    }

    [Fact]
    public void Each_cable_change_is_raised()
    {
        using var events = Create();

        _status = PowerLineStatus.Offline;
        Raise(events, PowerModes.StatusChange);
        _status = PowerLineStatus.Online;
        Raise(events, PowerModes.StatusChange);

        _changes.Should().Be(2);
        _log.Lines.Should().EndWith("INFO Güç: kaynak Battery→Ac");
    }

    [Fact]
    public void An_unreadable_source_reads_as_unknown_and_warns()
    {
        using var events = new SystemPowerEvents(_log, () => throw new InvalidOperationException("yok"));

        events.Current.Should().BeNull();
        _log.Lines.Should().Contain("WARN Güç: kaynak okunamadı (yok)");
    }

    [Fact]
    public void Sleep_and_wake_are_raised_and_logged_without_reading_the_source()
    {
        using var events = Create();
        var raised = new List<string>();
        events.Suspending += () => raised.Add("uyku");
        events.Resumed += () => raised.Add("uyanış");
        _status = PowerLineStatus.Offline;

        Raise(events, PowerModes.Suspend);
        Raise(events, PowerModes.Resume);

        raised.Should().Equal("uyku", "uyanış");
        _changes.Should().Be(0);
        _log.Lines.Should().EndWith(["INFO Güç: uyku", "INFO Güç: uyanış"]);
    }

    [Fact]
    public void A_throwing_subscriber_is_logged_and_does_not_escape_the_system_events_thread()
    {
        using var events = Create();
        events.Changed += () => throw new InvalidOperationException("abone");
        _status = PowerLineStatus.Offline;

        var raise = () => Raise(events, PowerModes.StatusChange);

        raise.Should().NotThrow();
        _log.Lines.Should().EndWith("ERROR Güç olayı (StatusChange) işlenemedi abone");
    }

    private SystemPowerEvents Create()
    {
        var events = new SystemPowerEvents(_log, () => _status);
        events.Changed += () => _changes++;
        return events;
    }

    private static void Raise(SystemPowerEvents events, PowerModes mode) =>
        events.OnPowerModeChanged(events, new PowerModeChangedEventArgs(mode));
}
