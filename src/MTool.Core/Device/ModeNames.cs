namespace MTool.Core.Device;

/// <summary>The modes' names as the window, the tray and plan descriptions show them.</summary>
public static class ModeNames
{
    public static string Of(PerformanceMode mode) => mode switch
    {
        PerformanceMode.High => "Yüksek",
        PerformanceMode.Balanced => "Dengeli",
        PerformanceMode.Eco => "Pil",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string Of(FanMode mode) => mode switch
    {
        FanMode.Auto => "Otomatik",
        FanMode.Advanced => "Gelişmiş",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
