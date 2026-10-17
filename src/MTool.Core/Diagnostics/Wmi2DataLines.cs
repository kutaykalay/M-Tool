using System.Globalization;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Core.Diagnostics;

/// <summary>
/// The report's reading of the <c>Get_Data</c> answers at the pinned registers, next to the packet
/// reading, so the two sources can be compared in the first WMI2 report. Decoded as the live values
/// are (<see cref="Wmi2Family"/>, <see cref="Wmi2Rpm"/>), every value with its raw byte; the status
/// registers are left raw. Unverified on every model.
/// </summary>
internal static class Wmi2DataLines
{
    /// <summary>No lines when the readout has no <c>Get_Data</c> call; one line when a register did not answer.</summary>
    public static IEnumerable<string> For(Wmi2Readout readout, Func<string, string?, string> line)
    {
        if (!readout.Calls.Any(c => c.Method == Wmi2Calls.GetData))
        {
            return [];
        }

        var values = readout.DataValues();
        var answered = Wmi2Calls.DataRegisters.Count(values.ContainsKey);
        if (answered < Wmi2Calls.DataRegisters.Count)
        {
            return [line("Get_Data", $"incomplete, {answered} of {Wmi2Calls.DataRegisters.Count} registers answered")];
        }

        var registers = new Answers(values);
        var sensors = Wmi2Family.ReadSensors(registers);
        return
        [
            line("Get_Data", "the pinned EC registers, unverified"),
            line("Temperatures", $"CPU {Celsius(sensors.CpuTempC)} ({Raw(values, Wmi2Family.CpuTemperature)}), " +
                                 $"GPU {Celsius(sensors.GpuTempC)} ({Raw(values, Wmi2Family.GpuTemperature)})"),
            line("Fans", $"CPU {Fan(values, Wmi2Family.CpuFanPercent, Wmi2Family.CpuTach)}; GPU {Fan(values, Wmi2Family.GpuFanPercent, Wmi2Family.GpuTach)}"),
            line("Status", string.Join(", ", Wmi2Calls.StatusRegisters.Select(r => Raw(values, r)))),
        ];
    }

    private static string Celsius(int? value) => value is { } c ? Invariant($"{c} C") : "? C";

    private static string Fan(IReadOnlyDictionary<byte, byte> values, byte percent, byte tach)
    {
        var (high, low) = (values[tach], values[(byte)(tach + 1)]);
        var rpm = Wmi2Rpm.Resolve(high, low) is { } resolved ? Invariant($"{resolved} RPM") : "RPM unclear";
        return Invariant($"{values[percent]} % ({Raw(values, percent)}), {rpm} (0x{tach:X2}:0x{tach + 1:X2} = {high:X2} {low:X2})");
    }

    private static string Raw(IReadOnlyDictionary<byte, byte> values, byte register) => Invariant($"0x{register:X2} = {values[register]:X2}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>The answers as EC memory, so the live decoding reads them unchanged.</summary>
    private sealed class Answers(IReadOnlyDictionary<byte, byte> values) : IEcRegisters
    {
        public byte Read(byte register) => values[register];

        public IReadOnlyList<byte> ReadBlock(byte startRegister, int count) =>
            [.. Enumerable.Range(startRegister, count).Select(r => values[(byte)r])];
    }
}
