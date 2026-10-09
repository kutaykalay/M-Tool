using System.Globalization;
using MTool.App.Resources;
using MTool.Core.Device;
using MTool.Core.Sensors;

namespace MTool.App.Tray;

/// <summary>Tray icon tooltip text: profile, then one line per fan. Pure, so it is unit tested.</summary>
internal static class TrayTooltip
{
    /// <summary><c>NotifyIcon.Text</c> throws above this on .NET 10 (measured 2026-09-30).</summary>
    public const int MaxLength = 127;

    private const string Prefix = "M-Tool · ";

    /// <param name="showGpu">False on a single-fan model: no GPU line.</param>
    public static string Format(SensorReading reading, string profileName, bool showGpu)
    {
        var marker = reading.Status switch
        {
            SensorStatus.Stale => Strings.Tray_DataStale,
            SensorStatus.Paused => Strings.Tray_Paused,
            _ => "",
        };
        var body = reading.Snapshot is { } s
            ? $"\n{FanLine("CPU", s.CpuTempC, s.CpuRpm)}" + (showGpu ? $"\n{FanLine("GPU", s.GpuTempC, s.GpuRpm)}" : "")
            : "\n" + Strings.Tray_ReadingSensors;

        var room = MaxLength - Prefix.Length - marker.Length - body.Length;
        return Prefix + Shorten(profileName, room) + marker + body;
    }

    private static string FanLine(string name, int? tempC, int rpm) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} {tempC?.ToString(CultureInfo.InvariantCulture) ?? "—"}°C {rpm} rpm");

    private static string Shorten(string text, int room) =>
        text.Length <= room ? text : string.Concat(text.AsSpan(0, Math.Max(0, room - 1)), "…");
}
