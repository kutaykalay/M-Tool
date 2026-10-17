using System.Collections.Frozen;

namespace MTool.Core.Device.Config;

/// <summary>Register rules: no byte used twice, every byte reachable, the port kept to its ceiling.</summary>
public static partial class DeviceConfigValidator
{
    /// <summary>The only register Cooler Boost may use through the port.</summary>
    public const byte CoolerBoostPortRegister = 0x98;

    /// <summary>
    /// The only register the charge limit may use through the port. The WMI2 charge register (0xD7)
    /// stays off the port until the project's port rule says otherwise.
    /// </summary>
    public static IReadOnlySet<byte> ChargeLimitPortRegisters { get; } = new byte[] { 0xEF }.ToFrozenSet();

    /// <summary>Every register any config may send to the port: 0x98 and 0xEF. Fan tables never go through the port.</summary>
    public static IReadOnlySet<byte> PortCeiling { get; } = ChargeLimitPortRegisters.Append(CoolerBoostPortRegister).ToFrozenSet();

    /// <summary>Firmware strings, fan values and tables, and feature registers may not share a byte.</summary>
    private static void CheckRegistersUnique(DeviceConfig config, List<string> errors)
    {
        foreach (var group in UsedRegisters(config).GroupBy(u => u.Register).Where(g => g.Count() > 1).OrderBy(g => g.Key))
        {
            errors.Add($"0x{group.Key:X2} is used in more than one place: {string.Join(", ", group.Select(u => u.Owner))}.");
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
                .Append((fan.Temperature, $"{id} temperature"))
                .Append((fan.SpeedPercent, $"{id} speed percent"))
                .Concat(Span(fan.RpmHigh, 2, $"{id} RPM"))
                .Concat(Span(fan.UpThresholds.Start, fan.UpThresholds.Count, $"{id} up threshold"))
                .Concat(Span(fan.Speeds.Start, fan.Speeds.Count, $"{id} speed"));
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
                errors.Add("A record with the wmi1 interface needs a wmi1 field map.");
            }

            return;
        }

        var fields = config.Wmi1.Fields;
        foreach (var register in Duplicates(fields.Select(f => f.Register)))
        {
            errors.Add($"0x{register:X2} has two WMI fields.");
        }

        foreach (var (className, index) in Duplicates(fields.Select(f => (f.Class, f.Index))))
        {
            errors.Add($"{ConfigText.Show(className)}[{index}] is mapped to more than one register.");
        }

        foreach (var field in fields.Where(f => f.Index < 0 || !WmiClassPattern().IsMatch(f.Class)))
        {
            errors.Add($"0x{field.Register:X2}: WMI field \"{ConfigText.Show(field.Class)}[{field.Index}]\" is not valid.");
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
            errors.Add($"0x{register:X2} has no read path (neither a WMI field nor the port).");
        }
    }

    /// <summary>The port carries only Cooler Boost and the charge limit, each on its own register.</summary>
    private static void CheckPort(DeviceConfig config, List<string> errors)
    {
        foreach (var register in Duplicates(config.PortRegisters))
        {
            errors.Add($"portRegisters lists 0x{register:X2} twice.");
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
            errors.Add($"0x{register:X2} is a fan table register and cannot go through the port.");
            return;
        }

        if (!PortCeiling.Contains(register))
        {
            errors.Add($"0x{register:X2} is not in the port ceiling ({string.Join(", ", PortCeiling.Order().Select(r => $"0x{r:X2}"))}).");
        }

        if (wmiFields.Contains(register))
        {
            errors.Add($"0x{register:X2} is both in the port list and in the WMI map.");
        }

        if (features.CoolerBoost?.Register == register)
        {
            if (register != CoolerBoostPortRegister)
            {
                errors.Add($"coolerBoost can use the port only at 0x{CoolerBoostPortRegister:X2} (0x{register:X2}).");
            }
        }
        else if (features.ChargeLimit?.Register == register)
        {
            if (!ChargeLimitPortRegisters.Contains(register))
            {
                errors.Add($"chargeLimit can use the port only at {string.Join(" or ", ChargeLimitPortRegisters.Order().Select(r => $"0x{r:X2}"))} (0x{register:X2}).");
            }
        }
        else
        {
            errors.Add($"0x{register:X2} belongs to no feature (only coolerBoost and chargeLimit go through the port).");
        }
    }
}
