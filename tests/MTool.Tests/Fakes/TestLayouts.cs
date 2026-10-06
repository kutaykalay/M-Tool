using MTool.Core.Device;
using MTool.Core.Device.Config;

namespace MTool.Tests.Fakes;

/// <summary>The P65 layout as the app builds it: from the embedded record.</summary>
internal static class TestLayouts
{
    public static DeviceConfig P65Config { get; } = DeviceConfigLoader.LoadEmbedded(new ListLog()).Single(c => c.Id == "msi-p65-creator-9se");

    public static DeviceLayout P65 { get; } = DeviceLayout.From(P65Config);
}
