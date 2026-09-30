using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MTool.Core.Sensors;

namespace MTool.App.ViewModels;

/// <summary>Temperatures and fan RPM (RPM, not percent: the percent register does not show Cooler Boost).</summary>
public sealed partial class SensorsViewModel : ObservableObject
{
    private const string Missing = "—";

    public SensorsViewModel() => Apply(SensorReading.Initial);

    [ObservableProperty]
    public partial string CpuTemperature { get; private set; }

    [ObservableProperty]
    public partial string GpuTemperature { get; private set; }

    [ObservableProperty]
    public partial string CpuFan { get; private set; }

    [ObservableProperty]
    public partial string GpuFan { get; private set; }

    /// <summary>Empty while live; otherwise why the values may be old.</summary>
    [ObservableProperty]
    public partial string Freshness { get; private set; }

    [ObservableProperty]
    public partial bool IsStale { get; private set; }

    public void Apply(SensorReading reading)
    {
        var s = reading.Snapshot;
        CpuTemperature = Temperature(s?.CpuTempC);
        GpuTemperature = Temperature(s?.GpuTempC);
        CpuFan = Rpm(s?.CpuRpm);
        GpuFan = Rpm(s?.GpuRpm);
        IsStale = reading.Status is SensorStatus.Stale or SensorStatus.Paused;
        Freshness = reading.Status switch
        {
            SensorStatus.Waiting => "Okunuyor…",
            SensorStatus.Live => "",
            SensorStatus.Paused => "Duraklatıldı (uyku)",
            _ => reading.LastUpdated is { } at ? $"Veri eski ({at.ToLocalTime():HH:mm:ss})" : "Veri yok",
        };
    }

    private static string Temperature(int? celsius) =>
        celsius is { } c ? string.Create(CultureInfo.InvariantCulture, $"{c} °C") : Missing;

    private static string Rpm(int? rpm) =>
        rpm is { } r ? string.Create(CultureInfo.InvariantCulture, $"{r} rpm") : Missing;
}
