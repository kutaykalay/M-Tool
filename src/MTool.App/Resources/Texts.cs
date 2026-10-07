using MTool.Core.Device;

namespace MTool.App.Resources;

/// <summary>
/// Window and tray wording that depends on a value. <see cref="ModeNames"/> is log text and stays
/// English; the names the user reads come from the string resources.
/// </summary>
internal static class Texts
{
    public static string Mode(PerformanceMode mode) => mode switch
    {
        PerformanceMode.High => Strings.Mode_High,
        PerformanceMode.Balanced => Strings.Mode_Balanced,
        PerformanceMode.Eco => Strings.Mode_Eco,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
