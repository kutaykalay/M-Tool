namespace MTool.Core.Ec;

/// <summary>Raw byte access to the ACPI EC I/O ports (0x62 data, 0x66 command/status).</summary>
public interface IPortIo
{
    byte In(byte port);

    void Out(byte port, byte value);
}
