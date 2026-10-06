using System.Text.Json;
using System.Text.Json.Serialization;
using MTool.Core.Profiles;

namespace MTool.Core.Device.Config;

/// <summary>A fan curve as <c>[[up, speed], ...]</c> pairs, the idle point first with up 0.</summary>
internal sealed class FanCurveConverter : JsonConverter<FanCurve>
{
    public override FanCurve Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var pairs = JsonSerializer.Deserialize<int[][]>(ref reader, options)
            ?? throw new JsonException("Eğri boş (null) olamaz.");

        if (pairs.Any(p => p is not { Length: 2 }))
        {
            throw new JsonException("Eğrinin her noktası [eşik, hız] çifti olmalı.");
        }

        return FanCurve.Of([.. pairs.Select(p => (p[0], p[1]))]);
    }

    public override void Write(Utf8JsonWriter writer, FanCurve value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var point in value.Points)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(point.UpThresholdC);
            writer.WriteNumberValue(point.SpeedPercent);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }
}
