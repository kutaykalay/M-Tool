using MTool.Core.Device.Config;

namespace MTool.Core.Device;

/// <summary>The device layouts built into M-Tool. The session picks one by firmware (<see cref="DeviceSelection"/>).</summary>
public static class EmbeddedDevices
{
    public const string P65Id = "msi-p65-creator-9se";

    /// <exception cref="InvalidOperationException">The embedded P65 record did not load (the reason is logged).</exception>
    public static DeviceLayout LoadP65(IAppLog log) => Find(DeviceConfigLoader.LoadEmbedded(log), P65Id);

    /// <exception cref="InvalidOperationException">No record with this id, or it has no layout yet.</exception>
    public static DeviceLayout Find(IEnumerable<DeviceConfig> configs, string id)
    {
        if (configs.FirstOrDefault(c => c.Id == id) is not { } config)
        {
            throw new InvalidOperationException($"Gömülü cihaz kaydı {id} yüklenemedi; ayrıntı log'da.");
        }

        return LayoutOf(config);
    }

    /// <exception cref="InvalidOperationException">The record has no layout yet; the message is meant for people.</exception>
    public static DeviceLayout LayoutOf(DeviceConfig config)
    {
        try
        {
            return DeviceLayout.From(config);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }
}
