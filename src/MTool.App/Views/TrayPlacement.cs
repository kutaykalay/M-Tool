using System.Windows;

namespace MTool.App.Views;

/// <summary>Where the main window goes, in the window's own units; <see cref="MaxHeight"/> keeps it on screen.</summary>
internal sealed record TrayCorner(double Left, double Top, double MaxHeight);

/// <summary>
/// The bottom-right corner of the work area, next to the tray. Pure, so the scaling maths is tested
/// without a screen.
/// </summary>
internal static class TrayPlacement
{
    /// <summary>
    /// Takes the work area in device pixels and the window's current scale. WPF's
    /// <c>SystemParameters.WorkArea</c> keeps the scale the app started with, so after the user changes
    /// the display scale it would put the window off screen.
    /// A window taller than the work area is capped to it and starts at its top, title bar visible.
    /// </summary>
    public static TrayCorner Corner(Rect workAreaPixels, DpiScale dpi, Size window, double margin)
    {
        var area = new Rect(
            workAreaPixels.X / dpi.DpiScaleX,
            workAreaPixels.Y / dpi.DpiScaleY,
            workAreaPixels.Width / dpi.DpiScaleX,
            workAreaPixels.Height / dpi.DpiScaleY);
        var maxHeight = Math.Max(0, area.Height - (2 * margin));
        var height = Math.Min(window.Height, maxHeight);
        return new TrayCorner(
            Math.Max(area.Left + margin, area.Right - window.Width - margin),
            Math.Max(area.Top + margin, area.Bottom - height - margin),
            maxHeight);
    }
}
