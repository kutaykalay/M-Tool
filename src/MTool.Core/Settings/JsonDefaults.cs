using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTool.Core.Settings;

internal static class JsonDefaults
{
    /// <summary>Enums as names; a number (or a name that does not exist) makes the file unreadable.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
}
