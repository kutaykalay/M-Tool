using System.Collections.Frozen;

namespace MTool.Core.Device.Config;

/// <summary>Register rules: no byte used twice, every byte reachable, the port kept to its ceiling.</summary>
public static partial class DeviceConfigValidator
{
    /// <summary>The only register Cooler Boost may use through the port.</summary>
    public const byte CoolerBoostPortRegister = 0x98;

    /// <summary>The only registers the charge limit may use through the port: WMI1 (0xEF) and WMI2 (0xD7) models.</summary>
    public static IReadOnlySet<byte> ChargeLimitPortRegisters { get; } = new byte[] { 0xEF, 0xD7 }.ToFrozenSet();

    /// <summary>Every register any config may send to the port. Fan tables never go through the port.</summary>
    public static IReadOnlySet<byte> PortCeiling { get; } = ChargeLimitPortRegisters.Append(CoolerBoostPortRegister).ToFrozenSet();

    /// <summary>Firmware strings, fan values and tables, and feature registers may not share a byte.</summary>
    private static void CheckRegistersUnique(DeviceConfig config, List<string> errors)
    {
        foreach (var group in UsedRegisters(config).GroupBy(u => u.Register).Where(g => g.Count() > 1).OrderBy(g => g.Key))
        {
            errors.Add($"0x{group.Key:X2} birden çok yerde kullanılıyor: {string.Join(", ", group.Select(u => u.Owner))}.");
        }
    }

    /// <summary>Every byte the record names, with what it is; blocks are cut at 0xFF (an overflow is reported elsewhere).</summary>
    private static IEnumerable<(int Register, string Owner)> UsedRegisters(DeviceConfig config)
    {
        var location = config.FirmwareLocation;
        var used = Span(location.Version, location.VersionLength, "firmware")
            .Concat(Span(location.Date, location.DateLength, "firmware tarihi"));

        foreach (var fan in config.Fans)
        {
            var id = ConfigText.Show(fan.Id);
            used = used
                .Append((fan.Temperature, $"{id} sıcaklık"))
                .Append((fan.SpeedPercent, $"{id} hız yüzdesi"))
                .Concat(Span(fan.RpmHigh, 2, $"{id} RPM"))
                .Concat(Span(fan.UpThresholds.Start, fan.UpThresholds.Count, $"{id} yukarı eşik"))
                .Concat(Span(fan.Speeds.Start, fan.Speeds.Count, $"{id} hız"));
        }

        return used.Concat(FeatureRegisters(config.Features));
    }

    private static IEnumerable<(int Register, string Owner)> FeatureRegisters(FeatureSet features)
    {
        (byte? Register, string Owner)[] all =
        [
            (features.CoolerBoost?.Register, "coolerBoost"),
            (features.ChargeLimit?.Register, "chargeLimit"),
            (features.PerformanceMode?.Register, "performanceMode"),
            (features.FanMode?.Register, "fanMode"),
        ];
        return all.Where(f => f.Register is not null).Select(f => ((int)f.Register!.Value, f.Owner));
    }

    private static IEnumerable<(int Register, string Owner)> Span(byte start, int count, string owner) =>
        Enumerable.Range(start, Math.Clamp(count, 0, RegisterSpace - start)).Select(r => (r, owner));

    private static void CheckWmi1(DeviceConfig config, List<string> errors)
    {
        if (config.Wmi1 is null)
        {
            if (config.Interface == WmiInterface.Wmi1)
            {
                errors.Add("wmi1 arayüzlü kayıtta wmi1 alan haritası olmalı.");
            }

            return;
        }

        var fields = config.Wmi1.Fields;
        foreach (var register in Duplicates(fields.Select(f => f.Register)))
        {
            errors.Add($"0x{register:X2} için iki WMI alanı var.");
        }

        foreach (var (className, index) in Duplicates(fields.Select(f => (f.Class, f.Index))))
        {
            errors.Add($"{ConfigText.Show(className)}[{index}] birden çok register'a bağlı.");
        }

        foreach (var field in fields.Where(f => f.Index < 0 || !WmiClassPattern().IsMatch(f.Class)))
        {
            errors.Add($"0x{field.Register:X2}: WMI alanı \"{ConfigText.Show(field.Class)}[{field.Index}]\" geçersiz.");
        }

        if (config.Interface == WmiInterface.Wmi1)
        {
            CheckReachable(config, fields, errors);
        }
    }

    /// <summary>A WMI1 record must say how each byte it names is read: a WMI field or the port. Fails closed.</summary>
    private static void CheckReachable(DeviceConfig config, IReadOnlyList<WmiFieldSpec> fields, List<string> errors)
    {
        var reachable = fields.Select(f => (int)f.Register).Concat(config.PortRegisters.Select(r => (int)r)).ToHashSet();
        foreach (var register in UsedRegisters(config).Select(u => u.Register).Distinct().Where(r => !reachable.Contains(r)).Order())
        {
            errors.Add($"0x{register:X2} için okuma yolu yok (WMI alanı da port da değil).");
        }
    }

    /// <summary>The port carries only Cooler Boost and the charge limit, each on its own register.</summary>
    private static void CheckPort(DeviceConfig config, List<string> errors)
    {
        foreach (var register in Duplicates(config.PortRegisters))
        {
            errors.Add($"portRegisters içinde 0x{register:X2} iki kez var.");
        }

        var tables = config.Fans
            .SelectMany(f => Span(f.UpThresholds.Start, f.UpThresholds.Count, "").Concat(Span(f.Speeds.Start, f.Speeds.Count, "")))
            .Select(u => u.Register)
            .ToHashSet();
        var wmiFields = config.Wmi1?.Fields.Select(f => f.Register).ToHashSet() ?? [];

        foreach (var register in config.PortRegisters.Distinct())
        {
            CheckPortRegister(register, tables, wmiFields, config.Features, errors);
        }
    }

    private static void CheckPortRegister(byte register, HashSet<int> tables, HashSet<byte> wmiFields, FeatureSet features, List<string> errors)
    {
        if (tables.Contains(register))
        {
            errors.Add($"0x{register:X2} fan tablosu register'ı, porta yönlendirilemez.");
            return;
        }

        if (!PortCeiling.Contains(register))
        {
            errors.Add($"0x{register:X2} port tavanında değil ({string.Join(", ", PortCeiling.Order().Select(r => $"0x{r:X2}"))}).");
        }

        if (wmiFields.Contains(register))
        {
            errors.Add($"0x{register:X2} hem portta hem WMI haritasında.");
        }

        if (features.CoolerBoost?.Register == register)
        {
            if (register != CoolerBoostPortRegister)
            {
                errors.Add($"coolerBoost porttan yalnızca 0x{CoolerBoostPortRegister:X2} olabilir (0x{register:X2}).");
            }
        }
        else if (features.ChargeLimit?.Register == register)
        {
            if (!ChargeLimitPortRegisters.Contains(register))
            {
                errors.Add($"chargeLimit porttan yalnızca 0xEF ya da 0xD7 olabilir (0x{register:X2}).");
            }
        }
        else
        {
            errors.Add($"0x{register:X2} hiçbir özelliğe ait değil (porttan yalnızca coolerBoost ve chargeLimit gider).");
        }
    }
}
