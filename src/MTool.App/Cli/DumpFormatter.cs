using System.Text;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.App.Cli;

internal static class DumpFormatter
{
    /// <param name="match">Finds (and logs) the record the firmware matches.</param>
    /// <param name="portOpen">False in a WMI-only session: Cooler Boost and the charge limit are not read then.</param>
    public static string Format(
        P65Device device, DeviceLayout layout, Func<FirmwareInfo?, DeviceMatch> match, IEcRegisters ec, int recoveredFailures, bool portOpen)
    {
        var firmware = device.ReadFirmware();
        var sensors = device.ReadSensors();
        var curves = device.ReadFanCurves();

        var text = new StringBuilder()
            .AppendLine($"M-Tool EC dump  {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Firmware   : {firmware.Version} ({firmware.Date}) {FirmwareStatus(firmware, match(firmware))}")
            .AppendLine($"CPU        : {Temp(sensors.CpuTempC)}, fan %{sensors.CpuFanPercent}, {sensors.CpuRpm} RPM")
            .AppendLine($"GPU        : {Temp(sensors.GpuTempC)}, fan %{sensors.GpuFanPercent}, {sensors.GpuRpm} RPM")
            .AppendLine($"Registers  : {string.Join(' ', PortPairs(ec, layout, portOpen).Append(Pair(ec, layout.PerformanceMode)).Append(Pair(ec, layout.FanMode)))}");
        AppendCurve(text, "CPU egrisi", curves.Cpu, FactoryDefaults.CpuDownOffsets);
        AppendCurve(text, "GPU egrisi", curves.Gpu, FactoryDefaults.GpuDownOffsets);
        text.AppendLine("             (esik/inis; inis = esik - fabrika farki, EC'den okunmaz)");
        return text.AppendLine($"Kurtarilan okuma hatasi: {recoveredFailures}").ToString();
    }

    private static void AppendCurve(StringBuilder text, string title, FanCurve curve, IReadOnlyList<int> downOffsets)
    {
        var downs = curve.DownThresholdsC(downOffsets);
        var steps = curve.Points.Select((p, i) => i == 0
            ? $"0C->%{p.SpeedPercent}"
            : $"{p.UpThresholdC}/{downs[i - 1]}C->%{p.SpeedPercent}");
        text.AppendLine($"{title} : {string.Join("  ", steps)}");
    }

    private static string FirmwareStatus(FirmwareInfo firmware, DeviceMatch match) =>
        firmware.IsSupported ? "destekleniyor"
        : match.Kind == MatchKind.Family ? $"{match.DisplayName} ailesinden, dogrulanmadi, salt okunur"
        : "TANINMIYOR, salt okunur";

    private static string Temp(int? celsius) => celsius is { } value ? $"{value} C" : "gecersiz okuma";

    private static string Pair(IEcRegisters ec, byte register) => $"0x{register:X2}=0x{ec.Read(register):X2}";

    /// <summary>The port registers the model has; without the port they are named but not read.</summary>
    private static IEnumerable<string> PortPairs(IEcRegisters ec, DeviceLayout layout, bool portOpen) =>
        new[] { layout.CoolerBoost, layout.ChargeLimit }.OfType<byte>()
            .Select(register => portOpen ? Pair(ec, register) : $"0x{register:X2}=port kapali");
}
