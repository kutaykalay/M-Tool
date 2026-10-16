namespace MTool.Core.Ec;

/// <summary>
/// Calls one read method of MSI's WMI2 interface (<c>MSI_ACPI</c>). There is no write member, and
/// a <see cref="Wmi2Request"/> exists only for the calls in <see cref="Wmi2Calls.Allowed"/>.
/// </summary>
public interface IWmi2Methods
{
    /// <summary>The answer packet as it came (32 bytes on a well-formed answer).</summary>
    /// <exception cref="EcAccessException">The call failed or did not answer in time.</exception>
    byte[] Call(Wmi2Request request);
}
