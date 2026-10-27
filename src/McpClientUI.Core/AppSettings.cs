using System.Text.Json;

namespace McpClientUI;

/// <summary>Remembered between runs: the server URL, recent URLs and the window layout.</summary>
public sealed class AppSettings
{
    /// <summary>%AppData%\McpClientUI on Windows, ~/Library/Application Support/McpClientUI on macOS, ~/.config/McpClientUI on Linux.</summary>
    public static readonly string Folder = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "McpClientUI")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McpClientUI");

    /// <summary>Creates the settings folder, readable only by the user on macOS and Linux.</summary>
    public static void EnsureFolder()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Folder);
        else Directory.CreateDirectory(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Url { get; set; } = "http://localhost:5199/mcp";
    public List<string> RecentUrls { get; set; } = [];
    public int[]? WindowBounds { get; set; }
    public bool Maximized { get; set; }
    public int SidebarWidth { get; set; }

    /// <summary>Saved argument sets: "url|kind|key" → preset name → arguments JSON.</summary>
    public Dictionary<string, Dictionary<string, string>> Presets { get; set; } = [];

    public static string PresetKey(string url, McpItem item) => $"{url.Trim().TrimEnd('/')}|{item.Id}";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken settings file just means starting with defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            EnsureFolder();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth bothering the user about.
        }
    }

    public void RememberUrl(string url)
    {
        Url = url;
        RecentUrls.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase));
        RecentUrls.Insert(0, url);
        if (RecentUrls.Count > 10)
            RecentUrls.RemoveRange(10, RecentUrls.Count - 10);
    }
}
