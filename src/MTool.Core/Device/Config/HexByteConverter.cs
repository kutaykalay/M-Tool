using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MTool.Core.Device.Config;

/// <summary>
/// Register addresses and values as <c>"0x6A"</c> strings, the way references and dumps write them.
/// Nothing else is a byte: no plain numbers, no decimal strings, no whitespace.
/// </summary>
internal sealed partial class HexByteConverter : JsonConverter<byte>
{
    // \z, not $: $ also matches before a final line break.
    [GeneratedRegex(@"^0x[0-9A-Fa-f]{1,2}\z")]
    private static partial Regex HexPattern();

    public override byte Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is null || !HexPattern().IsMatch(text)
            || !byte.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            throw new JsonException($"Bayt \"0x00\"-\"0xFF\" biçiminde bir dizge olmalı ({reader.TokenType}: {ConfigText.Show(text)}).");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, byte value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"0x{value:X2}");
}
