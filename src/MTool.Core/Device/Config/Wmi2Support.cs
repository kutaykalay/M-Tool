using MTool.Core.Ec;

namespace MTool.Core.Device.Config;

/// <summary>What the WMI2 window says about the msi-ec list for the running firmware.</summary>
public enum Wmi2ListMatch
{
    /// <summary>Not in the list (or not a WMI2 laptop).</summary>
    None,

    /// <summary>The family is in the list, the exact firmware is not: reading only.</summary>
    Family,

    /// <summary>The exact firmware is in the list.</summary>
    Exact,

    /// <summary>The built-in list could not be loaded: nothing is matched and nothing may be written.</summary>
    Unavailable,
}

/// <summary>
/// The msi-ec list as the WMI2 window uses it (plan asama15 15b-4). Only a WMI2 laptop, one where the
/// probe found no WMI1 class, is matched at all; a list that cannot be loaded is no list, never a
/// partial or permissive one. Nothing here reads the EC, and the WMI1 path never depends on it.
/// </summary>
public static class Wmi2Support
{
    /// <summary>The list built into M-Tool, checked against the built-in device records; null (logged) when it cannot be loaded.</summary>
    public static Wmi2FirmwareList? LoadEmbedded(IAppLog log) =>
        TryLoad(() => Wmi2FirmwareList.LoadEmbedded(DeviceConfigLoader.LoadEmbedded(log)), log);

    /// <summary>
    /// The list, or null when it is missing, unreadable, breaks a rule or loading fails in any other way
    /// (a converter bug must not end the reports-only window); the reason goes to the log. Ignoring the
    /// list is the safe side: no match, no writes.
    /// </summary>
    public static Wmi2FirmwareList? TryLoad(Func<Wmi2FirmwareList> load, IAppLog log)
    {
        try
        {
            return load();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error("WMI2 firmware list could not be loaded; it is ignored (no match, no WMI2 writes).", ex);
            return null;
        }
    }

    /// <param name="detected">What the start-up probe found: anything but WMI2 is never matched.</param>
    /// <param name="list">Null when the list could not be loaded.</param>
    /// <param name="firmware">The EC firmware version read from the laptop; null before it is read.</param>
    public static Wmi2ListMatch Match(WmiInterface? detected, Wmi2FirmwareList? list, string? firmware)
    {
        if (detected != WmiInterface.Wmi2)
        {
            return Wmi2ListMatch.None;
        }

        if (list is null)
        {
            return Wmi2ListMatch.Unavailable;
        }

        return list.Match(Info(firmware)) switch
        {
            MatchKind.Exact => Wmi2ListMatch.Exact,
            MatchKind.Family => Wmi2ListMatch.Family,
            _ => Wmi2ListMatch.None,
        };
    }

    /// <summary>The mode a read performance byte stands for; family overrides count only on a WMI2 laptop with a loaded list.</summary>
    public static PerformanceMode? Performance(WmiInterface? detected, Wmi2FirmwareList? list, string? firmware, byte raw) =>
        detected == WmiInterface.Wmi2 && list is not null
            ? list.PerformanceOf(Info(firmware), raw)
            : Wmi2WriteCeiling.StandardModeOf(raw);

    private static FirmwareInfo? Info(string? firmware) => firmware is null ? null : new FirmwareInfo(firmware, "");
}
