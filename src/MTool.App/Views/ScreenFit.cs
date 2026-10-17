using System.Windows;

namespace MTool.App.Views;

/// <summary>Where a window goes, in the window's own units; <see cref="MaxHeight"/> keeps it on screen.</summary>
internal sealed record WindowPlace(double Left, double Top, double MaxHeight);

/// <summary>
/// Keeps a window on the live work area. Takes the work area in device pixels and the window's
/// current scale: WPF's <c>SystemParameters.WorkArea</c> keeps the scale the app started with, so
/// after the user changes the display scale it would put the window off screen. A window taller
/// than the work area is capped to it and starts at its top, title bar visible. Pure, so the
/// scaling maths is tested without a screen.
/// </summary>
internal static class ScreenFit
{
    /// <summary>The work area in the window's units.</summary>
    public static Rect InUnits(Rect workAreaPixels, DpiScale dpi) => new(
        workAreaPixels.X / dpi.DpiScaleX,
        workAreaPixels.Y / dpi.DpiScaleY,
        workAreaPixels.Width / dpi.DpiScaleX,
        workAreaPixels.Height / dpi.DpiScaleY);

    public static double MaxHeight(Rect area, double margin) => Math.Max(0, area.Height - (2 * margin));

    /// <summary>In the middle of the work area.</summary>
    public static WindowPlace Centered(Rect workAreaPixels, DpiScale dpi, Size window, double margin)
    {
        var area = InUnits(workAreaPixels, dpi);
        var maxHeight = MaxHeight(area, margin);
        var height = Math.Min(window.Height, maxHeight);
        return new WindowPlace(
            Math.Max(area.Left + margin, area.Left + ((area.Width - window.Width) / 2)),
            Math.Max(area.Top + margin, area.Top + ((area.Height - height) / 2)),
            maxHeight);
    }

    /// <summary>Where the window is, moved only as far as it must to be inside the work area.</summary>
    public static WindowPlace KeepInside(Rect workAreaPixels, DpiScale dpi, Rect window, double margin)
    {
        var area = InUnits(workAreaPixels, dpi);
        var maxHeight = MaxHeight(area, margin);
        var height = Math.Min(window.Height, maxHeight);
        return new WindowPlace(
            Inside(window.Left, area.Left + margin, area.Right - window.Width - margin),
            Inside(window.Top, area.Top + margin, area.Bottom - height - margin),
            maxHeight);
    }

    /// <summary>The start wins over the end, so a window too big for the area shows its title bar.</summary>
    private static double Inside(double value, double start, double end) => Math.Max(start, Math.Min(value, end));
}
