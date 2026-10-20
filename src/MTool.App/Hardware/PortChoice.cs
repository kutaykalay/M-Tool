using MTool.App.Resources;

namespace MTool.App.Hardware;

/// <summary>Why a session that wanted the raw port opened without it.</summary>
internal enum PortFallback
{
    None,
    UnverifiedFirmware,
    PawnIoMissing,
}

/// <summary>Whether a session opens the raw port, decided from the backend asked for, the firmware and PawnIO.</summary>
internal readonly record struct PortChoice(bool UsePort, PortFallback Fallback)
{
    /// <param name="portAllowed">The firmware is one a record was verified for writes on.</param>
    /// <param name="pawnIoInstalled">Asked only when the port is allowed and wanted.</param>
    /// <exception cref="PawnIoMissingException"><see cref="EcBackends.Hybrid"/> and PawnIO is not installed.</exception>
    public static PortChoice For(EcBackends backend, bool portAllowed, Func<bool> pawnIoInstalled)
    {
        if (backend == EcBackends.WmiOnly)
        {
            return new(UsePort: false, PortFallback.None);
        }

        if (!portAllowed)
        {
            return new(UsePort: false, PortFallback.UnverifiedFirmware);
        }

        if (pawnIoInstalled())
        {
            return new(UsePort: true, PortFallback.None);
        }

        return backend == EcBackends.PreferHybrid
            ? new(UsePort: false, PortFallback.PawnIoMissing)
            : throw new PawnIoMissingException();
    }

    /// <summary>For the window: what the user should know about how the session opened.</summary>
    public IReadOnlyList<string> Warnings(bool firmwareRead, EcBackends backend)
    {
        List<string> warnings = [];
        if (!firmwareRead && backend != EcBackends.WmiOnly)
        {
            warnings.Add(Strings.Session_FirmwareUnread);
        }

        if (Fallback == PortFallback.PawnIoMissing)
        {
            warnings.Add(string.Format(Strings.Session_PawnIoMissing, PawnIoInstallation.InstallCommand));
        }

        return warnings;
    }

    /// <summary>Ends the log line that names the session's backend.</summary>
    public string LogSuffix => Fallback switch
    {
        PortFallback.UnverifiedFirmware => " (unverified firmware: port closed)",
        PortFallback.PawnIoMissing => " (PawnIO not installed: port closed)",
        _ => "",
    };
}
