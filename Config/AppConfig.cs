using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadCRHelper.Config;

/// <summary>
/// Everything read from RadCRHelper.json. Property names match the JSON (case-insensitive).
/// Every property has a default, so a missing or broken file still gives a working app.
/// </summary>
public sealed class AppConfig
{
    /// <summary>The newest settings-file shape this build understands.</summary>
    public const int SupportedConfigVersion = 1;

    public const string FileName = "RadCRHelper.json";

    public int ConfigVersion { get; set; } = SupportedConfigVersion;
    public PanelSettings Panel { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public SessionSettings Session { get; set; } = new();
    public List<HotkeySetting> Hotkeys { get; set; } = new() { new() { Keys = "Ctrl+Alt+H", Action = "panel.toggle" } };

    /// <summary>Quick-launch buttons (Zoom meetings, web pages). Shown in the panel's Meetings section.</summary>
    public List<LinkSetting> Links { get; set; } = new();
    public Dictionary<string, RoomSettings> Rooms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---- Filled in by Load(), not from JSON ----

    /// <summary>Settings for THIS computer (matched on computer name), or empty defaults.</summary>
    [JsonIgnore] public RoomSettings ThisRoom { get; private set; } = new();

    /// <summary>A plain-language problem with the settings file, or null if it loaded cleanly.</summary>
    [JsonIgnore] public string? LoadWarning { get; private set; }

    [JsonIgnore] public string FilePath { get; private set; } = "";

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        AppConfig config;
        string? warning = null;

        try
        {
            if (File.Exists(path))
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    Converters = { new JsonStringEnumConverter() },
                };
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), options) ?? new AppConfig();
            }
            else
            {
                config = new AppConfig();
                warning = $"Settings file not found ({FileName}). Using defaults.";
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings file", ex);
            config = new AppConfig();
            warning = $"Settings file could not be read. Using defaults. ({ex.Message})";
        }

        if (config.ConfigVersion > SupportedConfigVersion)
            warning = $"Settings file is newer (v{config.ConfigVersion}) than this app understands (v{SupportedConfigVersion}). Update the app.";

        // A section written as null in the file falls back to defaults instead of crashing later.
        config.Panel ??= new PanelSettings();
        config.Display ??= new DisplaySettings();
        config.Session ??= new SessionSettings();
        config.Hotkeys ??= new List<HotkeySetting>();
        config.Hotkeys.RemoveAll(h => h is null);
        config.Links ??= new List<LinkSetting>();
        config.Links.RemoveAll(l => l is null || string.IsNullOrWhiteSpace(l.Label) || !LinkSetting.IsAllowedUrl(l.Url));
        foreach (var link in config.Links)
            if (string.IsNullOrWhiteSpace(link.Id)) link.Id = LinkSetting.Slug(link.Label);
        config.Rooms ??= new Dictionary<string, RoomSettings>();

        // Rebuild with a case-insensitive lookup; the deserializer creates its own dictionary.
        config.Rooms = config.Rooms
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        config.ThisRoom = config.Rooms.TryGetValue(Environment.MachineName, out var room) ? room : new RoomSettings();
        config.LoadWarning = warning;
        config.FilePath = path;

        if (warning is not null) Log.Warn(warning);
        return config;
    }
}

public sealed class PanelSettings
{
    public double WidthDip { get; set; } = 320;
    public double TabWidthDip { get; set; } = 22;
    public double TabHeightDip { get; set; } = 110;
    public bool StartExpanded { get; set; } = true;
    public bool HideFromScreenShare { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#FFCD00";
}

public enum StartupDisplayMode { None, Mirror, Extend }

public sealed class DisplaySettings
{
    public StartupDisplayMode DefaultModeAtLogin { get; set; } = StartupDisplayMode.Mirror;
    public int RevertSeconds { get; set; } = 15;
}

public sealed class SessionSettings
{
    public int CountdownSeconds { get; set; } = 30;
}

public sealed class HotkeySetting
{
    public string Keys { get; set; } = "";
    public string Action { get; set; } = "";
}

public sealed class LinkSetting
{
    /// <summary>Action name becomes "link.&lt;id&gt;" (for hotkeys / macro deck). Made from the label if left out.</summary>
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";
    public string? Subtitle { get; set; }
    public string Url { get; set; } = "";

    /// <summary>WPF-UI Fluent icon name, e.g. Video24, Globe24. Defaults to Video24.</summary>
    public string? Icon { get; set; }

    public static bool IsAllowedUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme is "http" or "https" or "zoommtg" or "zoomus" or "msteams");

    public static string Slug(string text) =>
        string.Join("-", new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

public sealed class RoomSettings
{
    public string? RoomName { get; set; }

    /// <summary>Overrides display.defaultModeAtLogin for this computer only (e.g. None on a developer's desk).</summary>
    public StartupDisplayMode? DefaultModeAtLogin { get; set; }

    /// <summary>Desk monitor name (or part of it) as shown in Room Info. Kept as the main screen in Extend.</summary>
    public string? PresenterDisplay { get; set; }

    public bool HideDisplayControls { get; set; }
}
