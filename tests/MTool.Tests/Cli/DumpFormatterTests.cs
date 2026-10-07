using MTool.App.Cli;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Cli;

/// <summary>What <c>--dump</c> says about the record it read with (7f).</summary>
public class DumpFormatterTests
{
    private static readonly IReadOnlyList<DeviceConfig> Catalog = DeviceConfigLoader.LoadEmbedded(new ListLog());

    private static string Dump(DeviceLayout layout, FakeEcRegisters? ec = null)
    {
        ec ??= P65Memory.FactorySnapshot();
        return DumpFormatter.Format(new P65Device(ec, layout), layout, _ => DeviceMatch.None, ec, 0, portOpen: false);
    }

    [Fact]
    public void A_draft_record_is_named_as_experimental()
    {
        Dump(DeviceLayout.From(Catalog.Single(c => c.Id == EmbeddedDevices.Wmi1GenericId)).WithoutPortFeatures())
            .Should().Contain("Kayit      : MSI WMI1 (generic), deneysel, dogrulanmadi");
    }

    [Fact]
    public void The_p65_record_is_named_without_a_note()
    {
        var text = Dump(TestLayouts.P65);

        text.Should().Contain("Kayit      : MSI P65 Creator 9SE").And.NotContain("deneysel").And.NotContain("gecersiz tablo");
    }

    [Fact]
    public void An_implausible_table_is_marked()
    {
        var ec = P65Memory.FactorySnapshot();
        ec.Load(0x6A, 90, 40, 40, 200, 82, 88); // CPU up thresholds that cannot be real

        Dump(TestLayouts.P65, ec).Should().Contain("CPU egrisi : gecersiz tablo");
    }

    [Fact]
    public void Without_the_port_its_registers_are_named_but_not_read()
    {
        Dump(TestLayouts.P65.WithoutPortFeatures()).Should().Contain("0x98=port kapali 0xEF=port kapali 0xF2=");
    }
}
