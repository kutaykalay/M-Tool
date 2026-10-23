#:project ../../src/MTool.Core/MTool.Core.csproj
#:property PublishAot=false

// Extracts the WMI2 firmware list from msi-ec's msi-ec.c (plan asama15 §4.1). Facts only: the
// firmware strings of the CONF_G2_* configs. Comments, model names and code are never carried over.
// Usage (from the repository root):
//   dotnet run tools/msiec-import/msiec-import.cs -- <msi-ec.c> <commit sha> <output.json>

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Ec;

if (args.Length != 3 || !Regex.IsMatch(args[1], "^[0-9a-f]{40}$"))
{
    Console.Error.WriteLine("Usage: msiec-import <msi-ec.c> <40-character commit sha> <output.json>");
    return 2;
}

var (sourcePath, commit, outputPath) = (args[0], args[1], args[2]);

try
{
    var code = CSource.StripComments(File.ReadAllText(sourcePath));
    var configs = CSource.G2Configs(code);
    G2Facts.CheckAllAgree(configs);

    var firmware = CSource.Firmware(code, configs);
    var json = Output.Build(firmware, commit, configs.Select(c => c.Name), Output.Existing(outputPath));

    // The app's own rules decide: a list the app would refuse is never written.
    var list = Wmi2FirmwareList.Load(json, DeviceConfigLoader.LoadEmbedded(new ConsoleLog()));
    File.WriteAllText(outputPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"{configs.Count} configs, {list.Exact.Count} firmware, {list.Families.Count} families, "
        + $"{list.FamilyOverrides.Count} family overrides kept -> {outputPath}");
    return 0;
}
catch (Exception ex) when (ex is ImportException or InvalidDataException or JsonException or IOException
    or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine($"Import stopped: {ex.Message}");
    return 1;
}

sealed class ImportException(string message) : Exception(message);

sealed class ConsoleLog : IAppLog
{
    public void Info(string message) => Console.WriteLine(message);

    public void Warn(string message) => Console.Error.WriteLine($"WARN {message}");

    public void Error(string message, Exception? exception = null) =>
        Console.Error.WriteLine(exception is null ? $"ERROR {message}" : $"ERROR {message} {exception.Message}");
}

sealed record G2Config(string Name, string AllowedArray, string Facts);

static partial class CSource
{
    [GeneratedRegex(@"static\s+struct\s+msi_ec_conf\s+CONF_(G2_\d+)\s+__initdata\s*=\s*\{")]
    private static partial Regex ConfigStart();

    [GeneratedRegex(@"\.allowed_fw\s*=\s*(ALLOWED_FW_G2_\d+)\s*,")]
    private static partial Regex AllowedField();

    [GeneratedRegex(@"""([^""\\]*)""")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\bCONF_(G2_\d+)\b")]
    private static partial Regex AnyConfigName();

    /// <summary>Drops // and /* */ comments outside string and character literals.</summary>
    public static string StripComments(string code)
    {
        var result = new StringBuilder(code.Length);
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] is '"' or '\'')
            {
                var end = LiteralEnd(code, i);
                result.Append(code, i, end - i + 1);
                i = end;
            }
            else if (code.AsSpan(i).StartsWith("//"))
            {
                i = code.IndexOf('\n', i) is var nl and >= 0 ? nl - 1 : code.Length;
            }
            else if (code.AsSpan(i).StartsWith("/*"))
            {
                var close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close >= 0 ? close + 1 : throw new ImportException("A /* comment is never closed.");
                result.Append(' ');
            }
            else
            {
                result.Append(code[i]);
            }
        }

        return result.ToString();
    }

    public static IReadOnlyList<G2Config> G2Configs(string code)
    {
        var configs = ConfigStart().Matches(code).Select(m =>
        {
            var body = Block(code, m.Index + m.Length - 1);
            var allowed = AllowedField().Match(body) is { Success: true } a
                ? a.Groups[1].Value
                : throw new ImportException($"CONF_{m.Groups[1].Value} names no ALLOWED_FW_G2_* list.");
            return new G2Config(m.Groups[1].Value, allowed, G2Facts.Of(m.Groups[1].Value, body));
        }).ToList();

        // A config declared in a form the pattern above misses would silently drop its firmware.
        var unparsed = AnyConfigName().Matches(code).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)
            .Except(configs.Select(c => c.Name), StringComparer.Ordinal).ToList();
        if (unparsed.Count > 0)
        {
            throw new ImportException($"Named but not parsed (declaration form not recognised): {string.Join(", ", unparsed.Select(n => $"CONF_{n}"))}.");
        }

        return configs.Count > 0 ? configs : throw new ImportException("No CONF_G2_* config found.");
    }

    public static IReadOnlyList<string> Firmware(string code, IReadOnlyList<G2Config> configs)
    {
        var claims = new List<(string Firmware, string Array)>();
        foreach (var array in configs.Select(c => c.AllowedArray).Distinct(StringComparer.Ordinal))
        {
            var body = AllowedArray(code, array);
            if (body.Contains('\\'))
            {
                throw new ImportException($"{array} has an escape sequence; firmware strings never do.");
            }

            var strings = StringLiteral().Matches(body).Select(m => m.Groups[1].Value).ToList();
            claims.AddRange(strings.Count > 0 ? strings.Select(s => (s, array)) : throw new ImportException($"{array} lists no firmware."));
        }

        var repeated = claims.GroupBy(c => c.Firmware, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(c => c.Array))})").ToList();
        if (repeated.Count > 0)
        {
            throw new ImportException($"Firmware listed more than once: {string.Join("; ", repeated)}.");
        }

        return [.. claims.Select(c => c.Firmware)];
    }

    /// <summary>The text of a { } block whose opening brace is at <paramref name="open"/>, without the braces.</summary>
    public static string Block(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] is '"' or '\'')
            {
                i = LiteralEnd(code, i);
            }
            else if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}' && --depth == 0)
            {
                return code[(open + 1)..i];
            }
        }

        throw new ImportException("A { block is never closed.");
    }

    private static string AllowedArray(string code, string name)
    {
        var start = Regex.Match(code, $@"static\s+const\s+char\s+\*\s*{Regex.Escape(name)}\s*\[\]\s+__initconst\s*=\s*\{{");
        return start.Success
            ? Block(code, start.Index + start.Length - 1)
            : throw new ImportException($"{name} not found (or its declaration form is not recognised).");
    }

    private static int LiteralEnd(string code, int open)
    {
        for (var i = open + 1; i < code.Length; i++)
        {
            if (code[i] == '\\')
            {
                i++;
            }
            else if (code[i] == code[open])
            {
                return i;
            }
        }

        throw new ImportException("A string literal is never closed.");
    }
}

/// <summary>
/// The five features every G2 config must state the same way, and the same way as M-Tool's code; if
/// one differs the import stops instead of merging (a split is a separate decision).
/// </summary>
static partial class G2Facts
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\.charge_control_address\s*=\s*(0x[0-9a-fA-F]+)\s*,")]
    private static partial Regex ChargeAddress();

    [GeneratedRegex(@"0[xX][0-9a-fA-F]+")]
    private static partial Regex HexLiteral();

    private static readonly string Expected = string.Join(" | ",
        $"charge={Hex(Wmi2WriteCeiling.ChargeLimitRegister)}",
        $"cooler_boost=.address={Hex(Wmi2WriteCeiling.CoolerBoostRegister)},.bit={System.Numerics.BitOperations.Log2(Wmi2WriteCeiling.CoolerBoostMask)},",
        $"shift_mode=.address={Hex(Wmi2WriteCeiling.PerformanceRegister)},.modes={{"
            + $"{{SM_TURBO_NAME,{Hex(Wmi2WriteCeiling.StandardPerformance[PerformanceMode.High])}}},"
            + $"{{SM_ECO_NAME,{Hex(Wmi2WriteCeiling.StandardPerformance[PerformanceMode.Eco])}}},"
            + $"{{SM_COMFORT_NAME,{Hex(Wmi2WriteCeiling.StandardPerformance[PerformanceMode.Balanced])}}},MSI_EC_MODE_NULL}},",
        "super_battery=.address=0xeb,.mask=0x0f,", // deferred; checked so a change is noticed
        $"fan_mode=.address={Hex(Wmi2WriteCeiling.FanModeRegister)},.modes={{"
            + $"{{FM_AUTO_NAME,{Hex(Wmi2WriteCeiling.FanModeValues[Wmi2FanMode.Auto])}}},"
            + $"{{FM_SILENT_NAME,{Hex(Wmi2WriteCeiling.FanModeValues[Wmi2FanMode.Silent])}}},"
            + "{FM_ADVANCED_NAME,0x8d},MSI_EC_MODE_NULL},");

    public static string Of(string config, string body)
    {
        var charge = ChargeAddress().Match(body) is { Success: true } c ? c.Groups[1].Value.ToLowerInvariant() : "missing";
        return string.Join(" | ",
            $"charge={charge}",
            $"cooler_boost={Field(config, body, "cooler_boost")}",
            $"shift_mode={Field(config, body, "shift_mode")}",
            $"super_battery={Field(config, body, "super_battery")}",
            $"fan_mode={Field(config, body, "fan_mode")}");
    }

    public static void CheckAllAgree(IReadOnlyList<G2Config> configs)
    {
        var differing = configs.Where(c => c.Facts != Expected).ToList();
        if (differing.Count > 0)
        {
            throw new ImportException("These configs differ from M-Tool's WMI2 map: "
                + string.Join("; ", differing.Select(c => $"CONF_{c.Name}: {c.Facts}")) + $". Expected: {Expected}");
        }
    }

    private static string Field(string config, string body, string name)
    {
        var start = Regex.Match(body, $@"\.{name}\s*=\s*\{{");
        return start.Success
            ? HexLiteral().Replace(Whitespace().Replace(CSource.Block(body, start.Index + start.Length - 1), ""), m => m.Value.ToLowerInvariant())
            : throw new ImportException($"CONF_{config} has no .{name}.");
    }

    private static string Hex(byte value) => $"0x{value:x2}";
}

static class Output
{
    private const string MsiEcSourcePrefix = "msi-ec ";

    // Relaxed escaping keeps hand-written source lines readable ('&', '+', '<' stay as they are).
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The hand-written parts of the current file (overrides and non-msi-ec sources), if it exists.</summary>
    public static (JsonObject Overrides, IReadOnlyList<string> OtherSources) Existing(string path)
    {
        if (!File.Exists(path))
        {
            return (new JsonObject(), []);
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new ImportException($"{path} is not a JSON object.");
        var overrides = root["familyOverrides"]?.DeepClone() as JsonObject ?? new JsonObject();
        var sources = root["sources"]?.AsArray().Select(s => s?.GetValue<string>() ?? "")
            .Where(s => !s.StartsWith(MsiEcSourcePrefix, StringComparison.Ordinal)).ToList() ?? [];
        return (overrides, sources);
    }

    public static string Build(
        IReadOnlyList<string> firmware, string commit, IEnumerable<string> configNames, (JsonObject Overrides, IReadOnlyList<string> OtherSources) existing)
    {
        var exact = firmware.Order(StringComparer.Ordinal).ToList();
        var families = exact.Where(f => f.Length >= DeviceConfigValidator.FamilyLength)
            .Select(f => f[..DeviceConfigValidator.FamilyLength]).Distinct(StringComparer.Ordinal).ToList();

        var lostFamilies = existing.Overrides.Select(o => o.Key).Where(k => !families.Contains(k, StringComparer.Ordinal)).ToList();
        if (lostFamilies.Count > 0)
        {
            throw new ImportException($"familyOverrides name families msi-ec no longer lists: {string.Join(", ", lostFamilies)}.");
        }

        var msiEc = $"{MsiEcSourcePrefix}{commit[..7]}, {string.Join(", ", configNames.Select(n => $"CONF_{n}"))}: "
            + "WMI2 firmware strings, extracted by tools/msiec-import (GPL-2.0-or-later; facts only)";

        var root = new JsonObject
        {
            ["schemaVersion"] = Wmi2FirmwareList.SupportedSchemaVersion,
            ["sources"] = new JsonArray([.. new[] { msiEc }.Concat(existing.OtherSources).Select(s => JsonValue.Create(s))]),
            ["exact"] = new JsonArray([.. exact.Select(f => JsonValue.Create(f))]),
            ["families"] = new JsonArray([.. families.Select(f => JsonValue.Create(f))]),
            ["familyOverrides"] = existing.Overrides,
        };

        return root.ToJsonString(Indented) + "\n";
    }
}
