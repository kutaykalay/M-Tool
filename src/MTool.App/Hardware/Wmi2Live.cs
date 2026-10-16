using MTool.Core;
using MTool.Core.Device.Config;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>
/// The gate before the WMI2 live read (the project's EC safety rules): the interface is probed again
/// here. On any answer other than WMI2, and when the probe fails, it opens nothing (fails closed;
/// unlike <see cref="WmiInterfaceCheck"/>, which lets a WMI1 start-up go on).
/// </summary>
internal static class Wmi2Live
{
    /// <typeparam name="T">A reference type, so that "nothing opened" can be null.</typeparam>
    /// <returns>The opened session, which the caller owns and disposes; or null.</returns>
    /// <exception cref="EcAccessException">Opening failed (thrown by <paramref name="open"/>).</exception>
    public static T? OpenIfWmi2<T>(Func<WmiInterface?> detect, Func<T> open, IAppLog log)
        where T : class
    {
        WmiInterface? detected;
        try
        {
            detected = detect();
        }
        catch (EcAccessException ex)
        {
            log.Warn($"WMI2 live read: interface not detected, nothing opened: {ex.Message}");
            return null;
        }

        return detected == WmiInterface.Wmi2 ? open() : null;
    }
}
