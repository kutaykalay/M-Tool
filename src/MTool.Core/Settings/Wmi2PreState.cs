using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Core.Settings;

public static class Wmi2PreStateCapture
{
    /// <summary>
    /// Reads the four WMI2 ceiling registers twice and fails if the passes disagree under each
    /// register's write mask, so a byte mangled by concurrent EC traffic can never become the permanent
    /// backup. Only the masked bits are compared: the EC changes the low 7 bits of 0x98 by itself, while
    /// bit 7 (Cooler Boost) and the other whole bytes must agree. The first pass's raw bytes are stored.
    /// </summary>
    /// <exception cref="EcAccessException">A register differed between the passes, or a read failed.</exception>
    public static PreMToolState Read(IEcRegisters ec, FirmwareInfo firmware, DateTimeOffset now)
    {
        var registers = Wmi2WriteCeiling.Registers;
        var first = registers.Select(ec.Read).ToArray();
        var second = registers.Select(ec.Read).ToArray();

        for (var i = 0; i < registers.Count; i++)
        {
            var mask = Wmi2WriteRules.MaskOf(registers[i]);
            var before = (byte)(first[i] & mask);
            var after = (byte)(second[i] & mask);
            if (before != after)
            {
                throw new EcAccessException(
                    $"WMI2 backup not taken: 0x{registers[i]:X2} differed between two reads (0x{before:X2} / 0x{after:X2} under mask 0x{mask:X2}).");
            }
        }

        var values = registers.Zip(first, (register, value) => new RegisterValue(register, value)).ToArray();
        return new PreMToolState(firmware.Version, firmware.Date, now, Array.AsReadOnly(values));
    }
}
