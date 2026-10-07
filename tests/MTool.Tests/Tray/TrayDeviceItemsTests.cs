using System.Windows.Forms;
using MTool.App.Tray;
using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Profiles;
using MTool.Core.Settings;
using MTool.Tests.Fakes;

namespace MTool.Tests.Tray;

/// <summary>The device part of the tray menu: profiles, Cooler Boost, performance (7d).</summary>
public sealed class TrayDeviceItemsTests : IDisposable
{
    private static readonly DeviceCapabilities P65 = TestLayouts.P65.Capabilities;

    private readonly string _folder = Directory.CreateTempSubdirectory("mtool-test-").FullName;
    private readonly FakeP65Control _control = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Builds the items for <paramref name="capabilities"/>, reads them with <paramref name="read"/>, then disposes them.</summary>
    private T Read<T>(DeviceCapabilities capabilities, Func<IReadOnlyList<ToolStripItem>, T> read)
    {
        _control.Access = _control.Access with { Capabilities = capabilities };
        var service = new ProfileService(_control, ProfileCatalog.BuiltIn, new SettingsStore(_folder), AppSettings.Default, new ListLog());
        var status = new StatusViewModel(new FakeNotifier(), new ImmediateDispatcher(), TimeProvider.System);
        var items = TrayDeviceItems.For(new ControlsViewModel(service, _control, status, new ImmediateDispatcher()));
        try
        {
            return read(items);
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    private string[] Texts(DeviceCapabilities capabilities) =>
        Read(capabilities, items => items.Select(i => i is ToolStripSeparator ? "---" : i.Text ?? "").ToArray());

    [Fact]
    public void The_P65_menu_is_what_it_was()
    {
        Texts(P65).Should().Equal("Default", "Cool", "Silent", "---", "Cooler Boost", "Performans: fabrika ayarı (0x00)");
    }

    [Fact]
    public void Performance_lists_the_record_modes_in_order()
    {
        var modes = Read(P65 with { PerformanceModes = [PerformanceMode.Eco, PerformanceMode.High] },
            items => ((ToolStripMenuItem)items[^1]).DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).ToArray());

        modes.Should().Equal("Pil", "Yüksek");
    }

    [Fact]
    public void A_model_without_cooler_boost_has_no_item_for_it()
    {
        Texts(P65 with { CoolerBoost = false }).Should().NotContain("Cooler Boost");
    }

    [Fact]
    public void A_model_without_performance_modes_has_no_performance_menu()
    {
        Texts(P65 with { PerformanceModes = [] }).Should().NotContain(t => t.StartsWith("Performans", StringComparison.Ordinal));
    }

    [Fact]
    public void A_model_without_fan_tables_has_no_profile_items()
    {
        Texts(P65 with { FanCurve = false }).Should().Equal("Cooler Boost", "Performans: fabrika ayarı (0x00)");
    }

    [Fact]
    public void A_model_with_none_of_them_adds_nothing()
    {
        Texts(P65 with { FanCurve = false, CoolerBoost = false, PerformanceModes = [] }).Should().BeEmpty();
    }
}
