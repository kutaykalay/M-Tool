namespace MTool.Core.Device;

/// <summary>One instance of an MSI WMI1 class: the EC field at <paramref name="Index"/>.</summary>
public sealed record WmiField(string ClassName, int Index)
{
    public override string ToString() => $"{ClassName}[{Index}]";
}

/// <summary>
/// Which WMI1 field reaches each EC register M-Tool uses, for firmware 16Q4EMS2.107. Read from this
/// laptop's DSDT (WQAx/WSAx methods) and checked on hardware: firmware text, factory fan tables,
/// modes and RPM matched the port dump. Cooler Boost and the charge limit have no WMI1 field and
/// stay on the port (<see cref="PortRegisters"/>); every other register is unreachable.
/// </summary>
public static class WmiMap
{
    public const string Software = "MSI_Software";
    public const string Cpu = "MSI_CPU";
    public const string Vga = "MSI_VGA";
    public const string Ap = "MSI_AP";
    public const string System = "MSI_System";

    private const int FirmwareStartIndex = 6;
    private const int TemperatureIndex = 1;
    private const int SpeedPercentIndex = 2;
    private const int UpThresholdsStartIndex = 5;
    private const int SpeedsStartIndex = 11;

    public static IReadOnlyDictionary<byte, WmiField> Fields { get; } = Build();

    public static IReadOnlySet<byte> PortRegisters { get; } = new HashSet<byte> { EcMap.CoolerBoost, EcMap.ChargeLimit };

    private static Dictionary<byte, WmiField> Build()
    {
        var fields = new Dictionary<byte, WmiField>();
        AddRange(fields, EcMap.FirmwareVersion, EcMap.FirmwareVersionLength + EcMap.FirmwareDateLength, Software, FirmwareStartIndex);
        AddFan(fields, EcMap.CpuFan, Cpu);
        AddFan(fields, EcMap.GpuFan, Vga);

        // RPM bytes run backwards: MSI_AP[2..5] = 0xCD, 0xCC, 0xCB, 0xCA.
        fields.Add(0xCD, new WmiField(Ap, 2));
        fields.Add(0xCC, new WmiField(Ap, 3));
        fields.Add(0xCB, new WmiField(Ap, 4));
        fields.Add(0xCA, new WmiField(Ap, 5));

        fields.Add(EcMap.PerformanceMode, new WmiField(System, 7));
        fields.Add(EcMap.FanMode, new WmiField(System, 9));
        return fields;
    }

    /// <summary>MSI_CPU and MSI_VGA share one layout.</summary>
    private static void AddFan(Dictionary<byte, WmiField> fields, FanRegisters fan, string className)
    {
        fields.Add(fan.Temperature, new WmiField(className, TemperatureIndex));
        fields.Add(fan.SpeedPercent, new WmiField(className, SpeedPercentIndex));
        AddRange(fields, fan.UpThresholdsStart, EcMap.ThresholdCount, className, UpThresholdsStartIndex);
        AddRange(fields, fan.SpeedsStart, EcMap.SpeedCount, className, SpeedsStartIndex);
    }

    private static void AddRange(Dictionary<byte, WmiField> fields, byte start, int count, string className, int startIndex)
    {
        for (var i = 0; i < count; i++)
        {
            fields.Add((byte)(start + i), new WmiField(className, startIndex + i));
        }
    }
}
