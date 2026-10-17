using System.Runtime.InteropServices;

namespace MTool.App.Reports;

/// <summary>
/// Opens an address or a folder through the desktop's own Explorer, so it runs with the user's
/// normal rights and not with M-Tool's administrator rights (a browser started by an elevated
/// process is elevated too). The way in is the documented shell automation chain: the desktop's
/// window in <c>ShellWindows</c>, its top-level browser, the active view, and that view's
/// <c>Shell.Application</c>, whose <c>ShellExecute</c> runs inside Explorer (Raymond Chen, "How can
/// I launch an unelevated process from my elevated process", The Old New Thing, 2013). The interface
/// ids and vtable order are those of the Windows SDK (shobjidl.h, shldisp.h).
/// </summary>
internal static class DesktopShell
{
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvgioBackground = 0;
    private const int SwShowNormal = 1;
    private static readonly Guid TopLevelBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserId = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid DispatchId = new("00020400-0000-0000-C000-000000000046");

    /// <summary>
    /// Asks Explorer to open <paramref name="target"/> (an https address or a folder path), passed as the
    /// file and nothing else: no arguments, no command line.
    /// </summary>
    /// <returns>False when the desktop shell cannot be reached (Explorer not running, another shell); nothing was started.</returns>
    /// <exception cref="COMException">Explorer was reached but could not open it; not a reason to open it elevated instead.</exception>
    public static bool TryOpen(string target)
    {
        if (DesktopApplication() is not { } shell)
        {
            return false;
        }

        shell.ShellExecute(target, "", "", "open", SwShowNormal);
        return true;
    }

    /// <summary>The desktop Explorer's <c>Shell.Application</c>; null when any step of the way there fails.</summary>
    private static IShellDispatch2? DesktopApplication()
    {
        try
        {
            var windows = (IShellWindows)new ShellWindowsClass();
            object location = CsidlDesktop;
            object? root = null; // VT_EMPTY: the root is not used for the desktop
            var desktop = (IServiceProvider)windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch);
            var service = TopLevelBrowserService;
            var browserId = ShellBrowserId;
            var browser = (IShellBrowser)desktop.QueryService(ref service, ref browserId);
            var dispatchId = DispatchId;
            var view = (IShellFolderViewDual)browser.QueryActiveShellView().GetItemObject(SvgioBackground, ref dispatchId);
            return (IShellDispatch2)view.Application;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NullReferenceException)
        {
            return null;
        }
    }

    [ComImport]
    [Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ShellWindowsClass;

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellWindows
    {
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object? root,
            int windowClass,
            out int windowHandle,
            int options);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object QueryService(ref Guid service, ref Guid interfaceId);
    }

    /// <summary>Only QueryActiveShellView is called; the methods before it keep the vtable order.</summary>
    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        IShellView QueryActiveShellView();
    }

    /// <summary>Only GetItemObject is called; the methods before it keep the vtable order.</summary>
    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
        void SelectItem();

        [return: MarshalAs(UnmanagedType.Interface)]
        object GetItemObject(uint item, ref Guid interfaceId);
    }

    [ComImport]
    [Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellFolderViewDual
    {
        object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
    }

    [ComImport]
    [Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellDispatch2
    {
        void ShellExecute(
            [MarshalAs(UnmanagedType.BStr)] string file,
            [MarshalAs(UnmanagedType.Struct)] object arguments,
            [MarshalAs(UnmanagedType.Struct)] object directory,
            [MarshalAs(UnmanagedType.Struct)] object operation,
            [MarshalAs(UnmanagedType.Struct)] object show);
    }
}
