using System.Windows;

namespace MTool.App.Views;

/// <summary>
/// The bottom-right corner of the work area, next to the tray. Pure, so the scaling maths is tested
/// without a screen.
/// </summary>
internal static class TrayPlacement
{
    /// <summary>
    /// Takes the work area in device pixels and the window's current scale (see <see cref="ScreenFit"/>).
    /// A window taller than the work area is capped to it and starts at its top, title bar visible.
    /// </summary>
    public static WindowPlace Corner(Rect workAreaPixels, DpiScale dpi, Size window, double margin)
    {
        var area = ScreenFit.InUnits(workAreaPixels, dpi);
        var maxHeight = ScreenFit.MaxHeight(area, margin);
        var height = Math.Min(window.Height, maxHeight);
        return new WindowPlace(
            Math.Max(area.Left + margin, area.Right - window.Width - margin),
            Math.Max(area.Top + margin, area.Bottom - height - margin),
            maxHeight);
    }
}
