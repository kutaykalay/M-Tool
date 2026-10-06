using System.Text;
using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Profiles;

namespace MTool.App.Cli;

internal static class DumpFormatter
{
    public static string Format(P65Device device, DeviceLayout layout, IEcRegisters ec, int recoveredFailures)
    {
        var firmware = device.ReadFirmware();
        var sensors = device.ReadSensors();
        var curves = device.ReadFanCurves();

        var text = new StringBuilder()
            .AppendLine($"M-Tool EC dump  {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Firmware   : {firmware.Version} ({firmware.Date}) {(firmware.IsSupported ? "destekleniyor" : "TANINMIYOR, salt okunur")}")
            .AppendLine($"CPU        : {Temp(sensors.CpuTempC)}, fan %{sensors.CpuFanPercent}, {sensors.CpuRpm} RPM")
            .AppendLine($"GPU        : {Temp(sensors.GpuTempC)}, fan %{sensors.GpuFanPercent}, {sensors.GpuRpm} RPM")
            .AppendLine($"Registers  : {Pair(ec, layout.CoolerBoost)} {Pair(ec, layout.ChargeLimit)} " +
                        $"{Pair(ec, layout.PerformanceMode)} {Pair(ec, layout.FanMode)}");
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

    private static string Temp(int? celsius) => celsius is { } value ? $"{value} C" : "gecersiz okuma";

    private static string Pair(IEcRegisters ec, byte register) => $"0x{register:X2}=0x{ec.Read(register):X2}";
}
