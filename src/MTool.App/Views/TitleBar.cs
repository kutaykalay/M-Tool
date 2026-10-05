using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MTool.App.Views;

internal static class TitleBar
{
    private const int DwmUseImmersiveDarkMode = 20;

    /// <summary>Matches the title bar to the app theme (Windows 11 honours this attribute).</summary>
    public static void ApplyTheme(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
