namespace MTool.Core.Device;

/// <summary>The modes' names in plan descriptions and other log text: English in every UI language.</summary>
public static class ModeNames
{
    public static string Of(PerformanceMode mode) => mode switch
    {
        PerformanceMode.High => "High",
        PerformanceMode.Balanced => "Balanced",
        PerformanceMode.Eco => "Eco",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string Of(FanMode mode) => mode switch
    {
        FanMode.Auto => "Auto",
        FanMode.Advanced => "Advanced",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
