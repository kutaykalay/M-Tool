using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace MTool.App.Hardware;

/// <summary>
/// Copies an ACPI table out of the firmware Windows already loaded (<c>GetSystemFirmwareTable</c>).
/// A memory copy: no EC traffic, no driver, works without administrator rights.
/// </summary>
internal static class FirmwareTables
{
    private const uint AcpiProvider = 0x41435049; // 'ACPI'
    private const uint DsdtSignature = 0x54445344; // "DSDT" as the little-endian DWORD the API expects

    /// <summary>A DSDT is a few hundred KB (P65: 240,896 bytes); anything this big is not one.</summary>
    private const uint MaxTableBytes = 16 * 1024 * 1024;

    private const int TableHeaderBytes = 36;

    /// <exception cref="Win32Exception">The table is missing or could not be copied.</exception>
    /// <exception cref="InvalidDataException">The copy is not a DSDT.</exception>
    public static byte[] ReadDsdt()
    {
        var size = GetSystemFirmwareTable(AcpiProvider, DsdtSignature, null, 0);
        if (size == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "DSDT unreadable");
        }

        if (size is < TableHeaderBytes or > MaxTableBytes)
        {
            throw new InvalidDataException($"Unexpected DSDT size: {size} bytes");
        }

        var table = new byte[size];
        var copied = GetSystemFirmwareTable(AcpiProvider, DsdtSignature, table, size);
        if (copied != size)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"DSDT not copied ({copied}/{size} bytes)");
        }

        if (table[0] != 'D' || table[1] != 'S' || table[2] != 'D' || table[3] != 'T')
        {
            throw new InvalidDataException("The table read is not a DSDT.");
        }

        // The ACPI header's own length (offset 4) must agree with what Windows copied.
        if (BitConverter.ToUInt32(table, 4) != size)
        {
            throw new InvalidDataException($"The DSDT header length ({BitConverter.ToUInt32(table, 4)}) does not match the size read ({size}).");
        }

        return table;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint provider, uint tableId, byte[]? buffer, uint bufferSize);
}
