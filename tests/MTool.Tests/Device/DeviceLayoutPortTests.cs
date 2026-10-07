using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;
using static MTool.Tests.Device.Config.DeviceConfigFixtures;

namespace MTool.Tests.Device;

/// <summary>Cooler Boost and the charge limit are optional, and a session without the port hides them (7f).</summary>
public class DeviceLayoutPortTests
{
    private static DeviceConfig WithoutChargeLimit() => P65() with
    {
        Features = P65().Features with { ChargeLimit = null },
        PortRegisters = [0x98],
    };

    [Fact]
    public void A_record_without_a_charge_limit_has_a_layout()
    {
        var layout = DeviceLayout.From(WithoutChargeLimit());

        layout.ChargeLimit.Should().BeNull();
        layout.CoolerBoost.Should().Be(0x98);
        layout.Capabilities.ChargeLimit.Should().BeFalse();
        layout.WatchedRegisters.Should().Equal(TestLayouts.P65.WatchedRegisters);
    }

    [Fact]
    public void A_record_without_cooler_boost_has_a_layout()
    {
        var layout = DeviceLayout.From(P65() with { Features = P65().Features with { CoolerBoost = null }, PortRegisters = [0xEF] });

        layout.CoolerBoost.Should().BeNull();
        layout.Capabilities.CoolerBoost.Should().BeFalse();
    }

    [Fact]
    public void Without_the_port_the_port_features_are_hidden_and_nothing_else_changes()
    {
        var p65 = TestLayouts.P65;

        var layout = p65.WithoutPortFeatures();

        layout.Capabilities.Should().BeEquivalentTo(
            p65.Capabilities with { CoolerBoost = false, ChargeLimit = false }, o => o.WithStrictOrdering());
        layout.Should().BeEquivalentTo(p65, o => o.Excluding(l => l.Capabilities).WithStrictOrdering());
        layout.Wmi.Should().BeSameAs(p65.Wmi);
        p65.Capabilities.CoolerBoost.Should().BeTrue("the original stays as it was");
    }

    [Fact]
    public void Reading_the_port_state_without_a_charge_limit_is_refused()
    {
        var device = new P65Device(P65Memory.FactorySnapshot(), DeviceLayout.From(WithoutChargeLimit()));

        var act = () => device.ReadPortState();

        act.Should().Throw<InvalidOperationException>().WithMessage("*şarj limiti*");
    }

    [Fact]
    public void Reading_the_port_state_without_cooler_boost_is_refused()
    {
        var layout = DeviceLayout.From(P65() with { Features = P65().Features with { CoolerBoost = null }, PortRegisters = [0xEF] });

        var act = () => new P65Device(P65Memory.FactorySnapshot(), layout).ReadPortState();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cooler Boost*");
    }
}
