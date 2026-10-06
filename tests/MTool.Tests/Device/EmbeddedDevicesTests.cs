using MTool.Core.Device;
using MTool.Tests.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Device;

public class EmbeddedDevicesTests
{
    [Fact]
    public void The_p65_layout_loads_from_the_embedded_record_without_warnings()
    {
        var log = new ListLog();

        var layout = EmbeddedDevices.LoadP65(log);

        layout.Id.Should().Be(EmbeddedDevices.P65Id);
        layout.Wmi.Fields.Should().HaveCount(56);
        log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_record_is_a_clear_error()
    {
        var act = () => EmbeddedDevices.Find([DeviceConfigFixtures.Draft()], EmbeddedDevices.P65Id);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{EmbeddedDevices.P65Id}*yüklenemedi*");
    }

    [Fact]
    public void A_record_without_a_layout_is_a_clear_error_without_parameter_noise()
    {
        var single = DeviceConfigFixtures.Draft() with { Id = EmbeddedDevices.P65Id };

        var act = () => EmbeddedDevices.Find([single], EmbeddedDevices.P65Id);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("gpu").And.NotContain("Parameter");
    }
}
