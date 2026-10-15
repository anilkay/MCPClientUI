using System.Text.Json;

namespace McpClientUI;

/// <summary>Remembered between runs: the server URL, recent URLs and the window layout.</summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McpClientUI", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Url { get; set; } = "http://localhost:5199/mcp";
    public List<string> RecentUrls { get; set; } = [];
    public int[]? WindowBounds { get; set; }
    public bool Maximized { get; set; }
    public int SidebarWidth { get; set; }

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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
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
