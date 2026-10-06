using MTool.Core.Device.Config;

namespace MTool.Core.Device;

/// <summary>The device layouts built into M-Tool. Today only the P65; picking one by firmware comes later.</summary>
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
