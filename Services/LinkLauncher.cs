using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using RadCRHelper.Config;

namespace RadCRHelper.Services;

/// <summary>
/// Opens Quick links. For Outlook-style links it starts the browser directly instead of asking Windows,
/// which avoids the "How do you want to open this?" prompt.
///
/// edgeApp : Edge app window  -> Chrome app window  -> installed Outlook (if fallbackApp = outlook) -> default handler
/// edge    : Edge             -> Chrome             -> installed Outlook (if fallbackApp = outlook) -> default handler
/// default : whatever Windows does with the link
/// </summary>
public static class LinkLauncher
{
    /// <summary>Returns a short description of what opened (e.g. "Edge"), or null if nothing could open it.</summary>
    public static string? Open(LinkSetting link)
    {
        if (link.OpenWith == LinkOpenWith.Default)
            return ShellOpen(link.Url) ? "default" : null;

        bool appWindow = link.OpenWith == LinkOpenWith.EdgeApp;
        var args = appWindow ? $"--app={link.Url}" : link.Url;

        if (FindExe("msedge.exe", @"Microsoft\Edge\Application\msedge.exe") is { } edge && Start(edge, args))
            return "Edge";

        if (FindExe("chrome.exe", @"Google\Chrome\Application\chrome.exe") is { } chrome && Start(chrome, args))
            return "Chrome";

        if (string.Equals(link.FallbackApp, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            if (OpenOutlook(link.OutlookFolder) is { } outlook) return outlook;
            Log.Warn($"'{link.Label}': no Edge, Chrome or Outlook found");
            return null;
        }

        // No app fallback configured: let Windows try.
        return ShellOpen(link.Url) ? "default" : null;
    }

    /// <summary>Starts an installed program by exe name (App Paths, then the Microsoft 365 install folder).</summary>
    public static bool OpenLocalApp(string exeName)
    {
        var exe = FindExe(exeName, $@"Microsoft Office\root\Office16\{exeName}");
        if (exe is null)
        {
            Log.Warn($"{exeName} not found on this computer");
            return false;
        }
        return Start(exe, "");
    }

    /// <summary>Classic Outlook first (can jump to Calendar or Inbox), then the new Outlook app.</summary>
    private static string? OpenOutlook(string? folder)
    {
        if (FindExe("OUTLOOK.EXE", null) is { } classic)
        {
            var select = folder?.ToLowerInvariant() switch
            {
                "calendar" => "/select outlook:calendar",
                "inbox" => "/select outlook:inbox",
                _ => "",
            };
            if (Start(classic, select)) return "Outlook";
        }

        // New Outlook for Windows installs an execution alias here.
        var newOutlook = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\olk.exe");
        if (File.Exists(newOutlook) && Start(newOutlook, "")) return "Outlook";

        return null;
    }

    /// <summary>Looks in App Paths (per-machine and per-user), then the usual install folders.</summary>
    private static string? FindExe(string exeName, string? relativeInstallPath)
    {
        const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(appPaths + exeName);
                if (key?.GetValue(null) is string path)
                {
                    path = path.Trim('"');
                    if (File.Exists(path)) return path;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Reading App Paths for {exeName} failed: {ex.Message}");
            }
        }

        if (relativeInstallPath is null) return null;

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, relativeInstallPath);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static bool Start(string exe, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false });
            Log.Info($"Started {Path.GetFileName(exe)} {arguments}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Starting {exe} failed: {ex.Message}");
            return false;
        }
    }

    private static bool ShellOpen(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Opening {url} failed: {ex.Message}");
            return false;
        }
    }
}
