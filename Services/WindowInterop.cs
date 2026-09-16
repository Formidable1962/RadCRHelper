using System.Runtime.InteropServices;

namespace RadCRHelper.Services;

/// <summary>Small window tweaks WPF does not expose.</summary>
public static class WindowInterop
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 2004+

    /// <summary>Keeps the panel out of Alt+Tab.</summary>
    public static void HideFromAltTab(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW));
    }

    /// <summary>
    /// Hides the window from screen capture (Zoom, Teams, screenshots). It stays visible on every physical
    /// screen, including a mirrored TV. Returns false on Windows builds that do not support it.
    /// </summary>
    public static bool SetHiddenFromCapture(IntPtr hwnd, bool hidden)
    {
        bool ok = SetWindowDisplayAffinity(hwnd, hidden ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        if (!ok) Log.Warn($"SetWindowDisplayAffinity failed with Windows error {Marshal.GetLastWin32Error()}");
        return ok;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
