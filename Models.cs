using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;

namespace McpClientUI;

internal enum ItemKind { Tool, Resource, ResourceTemplate, Prompt }

/// <summary>
/// Something on the server that can be called: a tool, a resource, a resource template or a
/// prompt. Templates and prompts get a JSON schema built from their parameters so the same
/// argument form works for all of them.
/// </summary>
internal sealed class McpItem
{
    private static readonly JsonElement EmptySchema = JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() });

    private McpItem(ItemKind kind, string key, string name, string? title, string? description, string subtitle, JsonElement schema, object source)
    {
        Kind = kind;
        Key = key;
        Name = name;
        Title = string.IsNullOrWhiteSpace(title) ? null : title;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Subtitle = subtitle;
        Schema = schema;
        Source = source;
    }

    public ItemKind Kind { get; }
    /// <summary>Tool or prompt name, resource URI or URI template.</summary>
    public string Key { get; }
    public string Name { get; }
    public string? Title { get; }
    public string? Description { get; }
    /// <summary>Second line in the sidebar list.</summary>
    public string Subtitle { get; }
    public JsonElement Schema { get; }
    public object Source { get; }

    public bool HasArguments => Schema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object && p.EnumerateObject().Any();
    public string ActionText => Kind switch { ItemKind.Tool => "Run", ItemKind.Prompt => "Get", _ => "Read" };
    public string ResultTitle => Kind switch { ItemKind.Tool => "Result", ItemKind.Prompt => "Messages", _ => "Contents" };
    public string Id => $"{Kind}|{Key}";

    public static McpItem FromTool(McpClientTool tool) => new(ItemKind.Tool, tool.Name, tool.Name, tool.ProtocolTool.Title,
        tool.Description, FirstLine(tool.Description), tool.ProtocolTool.InputSchema, tool);

    public static McpItem FromResource(McpClientResource resource) => new(ItemKind.Resource, resource.Uri, resource.Name, resource.Title,
        resource.Description, resource.Uri, EmptySchema, resource);

    public static McpItem FromTemplate(McpClientResourceTemplate template)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, optional) in TemplateVariables(template.UriTemplate))
        {
            properties[name] = new JsonObject { ["type"] = "string", ["description"] = $"Value for {{{name}}} in {template.UriTemplate}" };
            if (!optional) required.Add(name);
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required };
        return new(ItemKind.ResourceTemplate, template.UriTemplate, template.Name, template.Title, template.Description,
            template.UriTemplate, JsonSerializer.SerializeToElement(schema), template);
    }

    public static McpItem FromPrompt(McpClientPrompt prompt)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var argument in prompt.ProtocolPrompt.Arguments ?? [])
        {
            var property = new JsonObject { ["type"] = "string" };
            if (!string.IsNullOrWhiteSpace(argument.Description)) property["description"] = argument.Description;
            properties[argument.Name] = property;
            if (argument.Required == true) required.Add(argument.Name);
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required };
        return new(ItemKind.Prompt, prompt.Name, prompt.Name, prompt.Title, prompt.Description,
            FirstLine(prompt.Description), JsonSerializer.SerializeToElement(schema), prompt);
    }

    /// <summary>RFC 6570 variables: {id} and {+path} are required, {?a,b} and {&amp;c} are optional query parameters.</summary>
    private static IEnumerable<(string Name, bool Optional)> TemplateVariables(string template)
    {
        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(template, @"\{([+#./;?&]?)([^}]+)\}"))
        {
            bool optional = m.Groups[1].Value is "?" or "&";
            foreach (var raw in m.Groups[2].Value.Split(','))
            {
                string name = raw.Trim().TrimEnd('*');
                int colon = name.IndexOf(':');
                if (colon >= 0) name = name[..colon];
                if (name.Length > 0 && seen.Add(name)) yield return (name, optional);
            }
        }
    }

    public static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "No description";
        text = text.Trim();
        int end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end];
    }
}

/// <summary>A row in a sidebar list.</summary>
internal sealed record ListEntry(string Title, string Subtitle, object Tag, Color? Dot = null, string? Trailing = null)
{
    public override string ToString() => Title;
}

/// <summary>One finished call, kept so it can be looked at again or repeated.</summary>
internal sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public ItemKind Kind { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "{}";
    public ResultStatus Status { get; set; }
    public string Meta { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Time { get; set; }
}

/// <summary>Call history in %AppData%\McpClientUI\history.json (newest first, capped).</summary>
internal sealed class HistoryStore
{
    private const int MaxEntries = 300;
    private const int MaxTextLength = 200_000;
    private static readonly string FilePath = Path.Combine(AppSettings.Folder, "history.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly List<HistoryEntry> entries;

    private HistoryStore(List<HistoryEntry> entries) => this.entries = entries;

    public static HistoryStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return new HistoryStore(JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath), Options) ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Start with an empty history.
        }
        return new HistoryStore([]);
    }

    public IEnumerable<HistoryEntry> ForServer(string url) =>
        entries.Where(e => string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));

    public void Add(HistoryEntry entry)
    {
        if (entry.Text.Length > MaxTextLength)
            entry.Text = entry.Text[..MaxTextLength] + "\n… (truncated in history)";
        entries.Insert(0, entry);
        if (entries.Count > MaxEntries)
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        Save();
    }

    public void Clear(string url)
    {
        entries.RemoveAll(e => string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is a convenience; losing it is not worth an error.
        }
    }
}

/// <summary>
/// HTTP headers (Bearer token, API keys, ...) per server URL, encrypted with DPAPI for the
/// current Windows user in %AppData%\McpClientUI\headers.dat.
/// </summary>
internal static class HeaderStore
{
    private static readonly string FilePath = Path.Combine(AppSettings.Folder, "headers.dat");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("McpClientUI headers");

    public static Dictionary<string, string> Get(string url) =>
        LoadAll().TryGetValue(Normalize(url), out var headers) ? headers : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static void Set(string url, Dictionary<string, string> headers)
    {
        var all = LoadAll();
        if (headers.Count == 0) all.Remove(Normalize(url));
        else all[Normalize(url)] = headers;
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(all);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Headers still apply for this session.
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(plain);
                if (all is not null)
                    return all.ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Unreadable (another user, corrupted): start empty.
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string url) => url.Trim().TrimEnd('/');
}
