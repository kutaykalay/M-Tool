using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTool.Core.Device.Config;

/// <summary>
/// Reads the device configs embedded in this assembly (<c>MTool.Devices.*.json</c>). A record that
/// cannot be read, fails <see cref="DeviceConfigValidator"/>, or shares an id or firmware with another
/// is skipped and logged; the rest still load, and the app never fails over a bad record.
/// </summary>
public static class DeviceConfigLoader
{
    public const string ResourcePrefix = "MTool.Devices.";

    private const int MaxJsonDepth = 16;

    /// <summary>
    /// Strict: unknown, missing or repeated fields, nulls where the type has none, enums as numbers and
    /// numbers as strings all make a record unreadable, so a typo cannot silently drop or replace a value.
    /// </summary>
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = MaxJsonDepth,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters =
        {
            new HexByteConverter(),
            new FanCurveConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    public static IReadOnlyList<DeviceConfig> LoadEmbedded(IAppLog log) => Load(EmbeddedSources(log), log);

    /// <param name="sources">Resource or file name (for the log) and its JSON text.</param>
    public static IReadOnlyList<DeviceConfig> Load(IEnumerable<(string Name, string Json)> sources, IAppLog log)
    {
        var valid = new List<(string Name, DeviceConfig Config)>();
        foreach (var (name, json) in sources)
        {
            if (TryRead(name, json, log) is { } config)
            {
                valid.Add((name, config));
            }
        }

        var conflicts = DeviceConfigValidator.FindConflicts([.. valid.Select(v => v.Config)]).ToDictionary(c => c.Id);
        foreach (var (name, config) in valid.Where(v => conflicts.ContainsKey(v.Config.Id)))
        {
            log.Warn($"Device record {name} skipped: {conflicts[config.Id].Reason}");
        }

        return [.. valid.Where(v => !conflicts.ContainsKey(v.Config.Id)).Select(v => v.Config)];
    }

    /// <exception cref="JsonException">The text is not a single, complete record.</exception>
    public static DeviceConfig Parse(string json) =>
        JsonSerializer.Deserialize<DeviceConfig>(json, Options) ?? throw new JsonException("Kayıt boş (null).");

    private static DeviceConfig? TryRead(string name, string json, IAppLog log)
    {
        try
        {
            var config = Parse(json);
            var errors = DeviceConfigValidator.Validate(config);
            if (errors.Count == 0)
            {
                return config;
            }

            log.Warn($"Device record {name} invalid: {string.Join(" ", errors)}");
        }
        catch (JsonException ex)
        {
            log.Warn($"Device record {name} unreadable: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A converter or rule that throws something unexpected must not take the app down with it.
            log.Error($"Device record {name} skipped after an unexpected error.", ex);
        }

        return null;
    }

    private static IEnumerable<(string Name, string Json)> EmbeddedSources(IAppLog log)
    {
        var assembly = typeof(DeviceConfigLoader).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                log.Warn($"Device record {name} could not be opened.");
                continue;
            }

            using var reader = new StreamReader(stream);
            yield return (name, reader.ReadToEnd());
        }
    }
}
