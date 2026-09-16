using System.Runtime.InteropServices;

namespace RadCRHelper.Services;

/// <summary>
/// Forced sign out and restart. These are presentation PCs: open programs are closed without
/// asking to save. There is deliberately NO shut down here — room PCs must stay on
/// (one room's PC is in a badge-access closet).
/// </summary>
public static class SessionService
{
    private const uint EWX_LOGOFF = 0x00000000;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_FORCE = 0x00000004;

    // "Other (Planned)" — shows up that way in the Windows event log.
    private const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    public static bool SignOut()
    {
        Log.Info("Sign out requested");
        return Exit(EWX_LOGOFF | EWX_FORCE);
    }

    public static bool Restart()
    {
        Log.Info("Restart requested");
        if (!EnableShutdownPrivilege())
            Log.Warn("Could not enable the shutdown privilege; trying restart anyway");
        return Exit(EWX_REBOOT | EWX_FORCE);
    }

    private static bool Exit(uint flags)
    {
        if (ExitWindowsEx(flags, SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_FLAG_PLANNED)) return true;
        Log.Error($"ExitWindowsEx(0x{flags:X}) failed with Windows error {Marshal.GetLastWin32Error()}");
        return false;
    }

    /// <summary>Standard users hold this privilege on workstations, but it starts disabled.</summary>
    private static bool EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out var luid)) return false;
            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);
}
