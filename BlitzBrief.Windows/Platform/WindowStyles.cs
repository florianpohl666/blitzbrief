using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlitzBrief.Windows.Platform;

/// <summary>
/// Erweiterte Fensterstile für Overlay-Fenster: WS_EX_NOACTIVATE (das Fenster nimmt der
/// Zielanwendung nie den Tastaturfokus) und optional WS_EX_TOOLWINDOW (kein Alt-Tab-Eintrag).
/// Frühestens ab OnSourceInitialized aufrufen – vorher existiert das HWND noch nicht.
/// </summary>
internal static class WindowStyles
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    public static void MakeNoActivate(Window window, bool toolWindow = false)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | (toolWindow ? WsExToolWindow : 0));
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
