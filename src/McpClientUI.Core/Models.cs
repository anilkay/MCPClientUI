using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;

namespace McpClientUI;

public enum ItemKind { Tool, Resource, ResourceTemplate, Prompt }

/// <summary>
/// Something on the server that can be called: a tool, a resource, a resource template or a
/// prompt. Templates and prompts get a JSON schema built from their parameters so the same
/// argument form works for all of them.
/// </summary>
public sealed class McpItem
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

/// <summary>One finished call, kept so it can be looked at again or repeated.</summary>
public sealed class HistoryEntry
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
public sealed class HistoryStore
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
            AppSettings.EnsureFolder();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is a convenience; losing it is not worth an error.
        }
    }
}

/// <summary>
/// HTTP headers (Bearer token, API keys, ...) or environment variables per server, encrypted for
/// the current user (see <see cref="Secrets"/>) in the settings folder's headers.dat.
/// </summary>
public static class HeaderStore
{
    private static readonly string FilePath = Path.Combine(AppSettings.Folder, "headers.dat");

    public static Dictionary<string, string> Get(string url) =>
        LoadAll().TryGetValue(Normalize(url), out var headers) ? headers : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static void Set(string url, Dictionary<string, string> headers)
    {
        var all = LoadAll();
        if (headers.Count == 0) all.Remove(Normalize(url));
        else all[Normalize(url)] = headers;
        try
        {
            AppSettings.EnsureFolder();
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(all);
            Secrets.WritePrivateFile(FilePath, Secrets.Protect(plain));
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
                byte[] plain = Secrets.Unprotect(File.ReadAllBytes(FilePath));
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

/// <summary>
/// What the address box points at: an http(s) URL (Streamable HTTP) or a command line that
/// starts a stdio server, such as <c>npx -y @modelcontextprotocol/server-everything</c>.
/// </summary>
public sealed class ServerTarget
{
    private ServerTarget(Uri? endpoint, string? command, List<string> arguments)
    {
        Endpoint = endpoint;
        Command = command;
        Arguments = arguments;
    }

    public Uri? Endpoint { get; }
    public string? Command { get; }
    public List<string> Arguments { get; }
    public bool IsStdio => Command is not null;

    /// <summary>Null when the text is empty or a URL that can't be used.</summary>
    public static ServerTarget? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (text.Contains("://"))
        {
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? new ServerTarget(uri, null, [])
                : null;
        }
        var parts = SplitCommandLine(text);
        return parts.Count == 0 ? null : new ServerTarget(null, parts[0], parts.Skip(1).ToList());
    }

    /// <summary>Splits a command line the way Windows programs do (quotes and backslashes included).</summary>
    private static List<string> SplitCommandLine(string text) =>
        OperatingSystem.IsWindows() ? SplitWindows(text) : SplitPosix(text);

    /// <summary>Shell-like splitting: spaces separate, '' and "" quote, a backslash escapes the next character.</summary>
    private static List<string> SplitPosix(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool inWord = false;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else if (c == '\\' && quote == '"' && i + 1 < text.Length && text[i + 1] is '"' or '\\') current.Append(text[++i]);
                else current.Append(c);
            }
            else if (c is '\'' or '"') { quote = c; inWord = true; }
            else if (c == '\\' && i + 1 < text.Length) { current.Append(text[++i]); inWord = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord) { parts.Add(current.ToString()); current.Clear(); inWord = false; }
            }
            else { current.Append(c); inWord = true; }
        }
        if (inWord) parts.Add(current.ToString());
        return parts;
    }

    private static List<string> SplitWindows(string text)
    {
        IntPtr argv = CommandLineToArgvW(text, out int count);
        if (argv == IntPtr.Zero) return [];
        try
        {
            var parts = new List<string>(count);
            for (int i = 0; i < count; i++)
                parts.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "");
            return parts;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

/// <summary>The last lines a stdio server wrote to stderr (shown when it fails to start).</summary>
public sealed class StderrTail
{
    private const int MaxLines = 15;
    private readonly Queue<string> lines = new();

    public void Add(string line)
    {
        lock (lines)
        {
            lines.Enqueue(line);
            while (lines.Count > MaxLines) lines.Dequeue();
        }
    }

    public string Text
    {
        get { lock (lines) return string.Join("\n", lines).Trim(); }
    }
}
