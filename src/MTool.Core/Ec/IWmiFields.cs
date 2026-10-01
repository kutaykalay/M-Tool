namespace MTool.Core.Ec;

/// <summary>
/// Raw access to MSI's WMI1 data blocks (<c>root\WMI</c>, <c>MSI_*</c>): each instance of a class is
/// one byte-sized EC field, addressed by its index. The BIOS methods behind it go through Windows'
/// own EC driver, so this access never races Windows the way the ports do. Like <see cref="IPortIo"/>,
/// it is only used beneath the EC worker; writes reach it through <see cref="EcGateway"/> alone.
/// </summary>
public interface IWmiFields
{
    /// <summary>
    /// Reads the fields of one class, in the order of <paramref name="indices"/>. Callers only ask
    /// for fields in <see cref="Device.WmiMap"/>; anything else is a programming error
    /// (<see cref="InvalidOperationException"/>), never retried.
    /// </summary>
    /// <exception cref="EcAccessException">WMI did not answer (timeout, service busy); safe to retry.</exception>
    IReadOnlyList<int> Read(string className, IReadOnlyList<int> indices);

    /// <summary>Writes one field. WMI checks nothing: the BIOS stores the value as is.</summary>
    /// <exception cref="EcAccessException">WMI did not answer; the gateway reads every write back.</exception>
    void Write(string className, int index, byte value);
}
