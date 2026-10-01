namespace MTool.Core.Device;

/// <summary>
/// Whether a call may use the raw port (Cooler Boost 0x98, charge limit 0xEF), which races
/// Windows' EC driver. No default anywhere: every caller says which it means, so an automatic
/// path cannot reach the port by leaving an argument out.
/// </summary>
public enum PortUse
{
    /// <summary>A user action: the port may be read and, for a reapply, written.</summary>
    Allowed,

    /// <summary>Start-up and resume: the port is neither read nor written; the cache answers.</summary>
    None,
}
