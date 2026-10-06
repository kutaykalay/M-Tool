using System.Windows.Forms;
using MTool.App.Services;
using MTool.Core.Power;

namespace MTool.Tests.Services;

public class SystemPowerEventsTests
{
    [Theory]
    [InlineData(PowerLineStatus.Online, PowerSource.Ac)]
    [InlineData(PowerLineStatus.Offline, PowerSource.Battery)]
    [InlineData(PowerLineStatus.Unknown, null)]
    [InlineData((PowerLineStatus)42, null)]
    public void Windows_power_line_status_maps_to_a_source_or_unknown(PowerLineStatus status, PowerSource? expected) =>
        SystemPowerEvents.From(status).Should().Be(expected);
}
