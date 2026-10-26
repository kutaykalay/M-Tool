namespace MTool.Core.Ec;

/// <summary>
/// Calls <c>MSI_ACPI.Set_Data</c>, the only WMI2 write M-Tool may make (CLAUDE.md, "WMI2 deneysel yazma").
/// Kept apart from <see cref="IWmi2Methods"/> so a read-only session has no write member at all.
/// </summary>
public interface IWmi2DataWriter
{
    /// <summary>The answer packet as it came; byte 0 is the OK flag (01) on a well-formed answer.</summary>
    /// <exception cref="EcAccessException">The call failed or did not answer in time.</exception>
    byte[] SetData(Wmi2WriteRequest request);
}
