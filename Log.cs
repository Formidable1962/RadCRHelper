using System.IO;

namespace RadCRHelper;

/// <summary>
/// Tiny append-only log at %LOCALAPPDATA%\RadCRHelper\RadCRHelper.log. Never throws.
/// Rolled over at 1 MB so a room PC never fills up.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadCRHelper");

    public static string FilePath { get; } = Path.Combine(Dir, "RadCRHelper.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level,-5} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
