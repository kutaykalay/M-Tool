using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MTool.Core.Device.Config;

namespace MTool.Core.Diagnostics;

/// <summary>
/// The text of <c>--report</c>: what an owner of another MSI model sends so M-Tool can learn it.
/// English, because it goes to a public GitHub issue. Pure, so it is unit tested.
/// </summary>
public static class DeviceReport
{
    public const string DsdtFileName = "dsdt.aml";

    private const string Hidden = "<hidden>";
    private const int MinPrivateWordLength = 2;

    /// <param name="privateWords">The user and machine names: masked wherever they appear, as a safety net.</param>
    public static string Format(DeviceReportInput input, IReadOnlyCollection<string> privateWords)
    {
        var text = new StringBuilder()
            .AppendLine("M-Tool device report")
            .AppendLine(Line("M-Tool", input.AppVersion))
            .AppendLine(Line("Generated", input.At.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)));
        AppendSystem(text, input.Computer);
        AppendWmi(text, input);
        text.AppendLine()
            .AppendLine("[ACPI]")
            .AppendLine(Line("DSDT", Describe(input.DsdtBytes, bytes => $"{bytes} bytes ({DsdtFileName})")));
        AppendEc(text, input.Ec);
        AppendWmi2(text, input);
        return Mask(text.ToString(), privateWords);
    }

    private static void AppendSystem(StringBuilder text, Section<SystemInfo> system)
    {
        text.AppendLine().AppendLine("[System]");
        if (system.Value is not { } s)
        {
            text.AppendLine(Line("System", $"not read ({system.Error})"));
            return;
        }

        text.AppendLine(Line("Manufacturer", s.Manufacturer))
            .AppendLine(Line("Model", s.Model))
            .AppendLine(Line("Baseboard", s.BaseBoard))
            .AppendLine(Line("BIOS", $"{s.BiosVersion ?? "?"} ({s.BiosDate ?? "?"})"))
            .AppendLine(Line("CPU", s.Cpu))
            .AppendLine(Line("Windows", s.Windows));
    }

    private static void AppendWmi(StringBuilder text, DeviceReportInput input)
    {
        text.AppendLine()
            .AppendLine("[MSI WMI]")
            .AppendLine(Line("Interface", Describe(input.Interface, Name)));
        if (input.Classes.Value is not { } classes)
        {
            text.AppendLine(Line("Classes", $"not read ({input.Classes.Error})"));
            return;
        }

        text.AppendLine(Line("Classes", classes.Count == 0 ? "none" : $"{classes.Count}"));
        foreach (var c in classes)
        {
            text.AppendLine($"  {c.Name}: {string.Join(", ", c.Properties)}");
        }
    }

    private static void AppendEc(StringBuilder text, Section<EcReadout> section)
    {
        text.AppendLine().AppendLine("[EC, WMI only, port not opened]");
        if (section.Value is not { } ec)
        {
            text.AppendLine(Line("EC", section.Error));
            return;
        }

        var verified = ec.Match.Kind == MatchKind.Exact ? "" : ", unverified on this model";
        text.AppendLine(Line("Firmware", ec.Firmware is { } f ? $"{f.Version} ({f.Date})" : "not read"))
            .AppendLine(Line("Match", ec.Match.Kind == MatchKind.None ? "none" : $"{ec.Match.Kind} ({ec.Match.RecordId})"))
            .AppendLine(Line("Read with", $"{ec.LayoutId} layout{verified}"))
            .AppendLine(Line("Capabilities", Capabilities(ec)))
            .AppendLine(Line("Fields", $"{ec.Fields.Readouts.Count}"));
        foreach (var field in ec.Fields.Readouts)
        {
            var value = field.Value is { } v ? $"0x{v:X2}" : "read failed";
            text.AppendLine($"  0x{field.Register:X2} {field.Field} = {value}");
        }

        if (ec.Fields.Skipped > 0)
        {
            text.AppendLine($"  {ec.Fields.Skipped} more fields skipped after {FieldScan.MaxFailuresInARow} failed reads in a row");
        }
    }

    /// <summary>Only on a WMI2 laptop, or when asked for: the raw packets, then a cautious reading of them.</summary>
    private static void AppendWmi2(StringBuilder text, DeviceReportInput input)
    {
        if (input.Wmi2 is null && input.Interface.Value != WmiInterface.Wmi2)
        {
            return;
        }

        text.AppendLine().AppendLine("[WMI2, Get_* methods only, raw]");
        if (input.Wmi2 is not { } section)
        {
            text.AppendLine(Line("WMI2", "not read: run M-Tool.exe --report --wmi2 to include the WMI2 packets"));
            return;
        }

        if (section.Value is not { } readout)
        {
            text.AppendLine(Line("WMI2", section.Error));
            return;
        }

        foreach (var call in readout.Calls)
        {
            var answer = call.Packet is { } packet ? string.Join(' ', packet.Select(b => $"{b:X2}")) : $"failed ({call.Error})";
            text.AppendLine($"  {call.Method}({call.Sub}) : {answer}");
        }

        var reading = readout.Interpret();
        text.AppendLine(Line("Reading", "YAMDCC's packet layout, unverified"))
            .AppendLine(Line("Firmware", reading.Firmware is { } f ? $"{f} ({reading.FirmwareDate})" : null))
            .AppendLine(Line("Temperatures", $"CPU {Show(reading.CpuTempC)} C, GPU {Show(reading.GpuTempC)} C"))
            .AppendLine(Line("Fans", $"CPU {Show(reading.CpuRpm)} RPM, GPU {Show(reading.GpuRpm)} RPM"));
    }

    private static string Show(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private static string Capabilities(EcReadout ec)
    {
        var c = ec.Capabilities;
        string?[] names =
        [
            c.GpuFan ? "GPU fan" : null,
            c.FanCurve ? "fan curve" : null,
            c.CoolerBoost ? "Cooler Boost" : null,
            c.ChargeLimit ? "charge limit" : null,
        ];
        return $"{string.Join(", ", names.OfType<string>())}; modes {string.Join(", ", c.PerformanceModes)}";
    }

    private static string Name(WmiInterface? wmi) => wmi switch
    {
        WmiInterface.Wmi1 => "WMI1",
        WmiInterface.Wmi2 => "WMI2",
        _ => "none",
    };

    private static string Describe<T>(Section<T> section, Func<T, string> describe) =>
        section.IsOk ? describe(section.Value!) : $"not read ({section.Error})";

    private static string Line(string label, string? value) => $"{label,-12} : {value ?? "?"}";

    /// <summary>Whole words only, ignoring case: a short name must not cut into other words.</summary>
    private static string Mask(string text, IReadOnlyCollection<string> privateWords)
    {
        foreach (var word in privateWords.Select(w => w.Trim()).Where(w => w.Length >= MinPrivateWordLength).OrderByDescending(w => w.Length))
        {
            text = Regex.Replace(text, $@"(?<![\w-]){Regex.Escape(word)}(?![\w-])", Hidden, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }
}
