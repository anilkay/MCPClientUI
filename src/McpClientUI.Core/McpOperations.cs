using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpClientUI;

/// <summary>The server's tools, resources (and templates) and prompts, each sorted by name.</summary>
public sealed record ServerItems(List<McpItem> Tools, List<McpItem> Resources, List<McpItem> Prompts)
{
    public static readonly ServerItems Empty = new([], [], []);
    public IEnumerable<McpItem> All => Tools.Concat(Resources).Concat(Prompts);
}

/// <summary>What a call returned, as text for the result view.</summary>
public sealed record CallOutcome(ResultStatus Status, string Text);

/// <summary>Talking to an MCP server; shared by both UIs.</summary>
public static class McpOperations
{
    /// <summary>How long connecting may take: stdio servers may be downloaded first (npx, uvx).</summary>
    public static TimeSpan ConnectTimeout(ServerTarget target) => target.IsStdio ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(20);

    public static IClientTransport CreateTransport(ServerTarget target, IReadOnlyDictionary<string, string> secrets, StderrTail stderr)
    {
        if (!target.IsStdio)
        {
            return new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = target.Endpoint!,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "MCP Client",
                AdditionalHeaders = secrets.Count > 0 ? secrets.ToDictionary() : null,
            });
        }

        var environment = secrets.ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        string command = target.Command!;
        if (!OperatingSystem.IsWindows())
        {
            // An app opened from Finder or a desktop launcher gets only the system PATH, so tools
            // installed with Homebrew, npm or pip (npx, uvx, node, python) would not be found.
            string path = ExtendedPath();
            environment.TryAdd("PATH", path);
            command = FindExecutable(command, path) ?? command;
        }
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = command,
            Arguments = target.Arguments,
            Name = "MCP Client",
            EnvironmentVariables = environment,
            StandardErrorLines = stderr.Add,
        });
    }

    public static async Task<McpClient> ConnectAsync(IClientTransport transport, CancellationToken cancellationToken) =>
        await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "mcp-client-ui", Version = "1.0" },
        }, cancellationToken: cancellationToken);

    /// <summary>Error text for a failed connection, with hints for missing credentials and stdio output.</summary>
    public static string DescribeConnectError(Exception ex, ServerTarget target, TimeSpan timeout, StderrTail stderr)
    {
        string message = ex is OperationCanceledException ? $"The server didn't answer within {timeout.TotalSeconds:0} seconds." : ex.Message;
        if (message.Contains("401") || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            message += "\n\nThe server wants credentials: add a Bearer token or API key header with the Headers button.";
        // The SDK already puts stderr into some messages (e.g. when the process exits); don't repeat it.
        if (target.IsStdio && stderr.Text is { Length: > 0 } output && !message.Contains(output.Split('\n')[^1]))
            message += "\n\nThe server wrote:\n" + output;
        return message;
    }

    public static string ServerName(McpClient client) =>
        string.IsNullOrWhiteSpace(client.ServerInfo.Title) ? client.ServerInfo.Name : client.ServerInfo.Title;

    /// <summary>Lists tools, resources (and templates) and prompts, each only if the server offers it.</summary>
    public static async Task<ServerItems> LoadItemsAsync(McpClient client, CancellationToken cancellationToken)
    {
        var caps = client.ServerCapabilities;
        var tools = new List<McpItem>();
        var resources = new List<McpItem>();
        var prompts = new List<McpItem>();

        if (caps?.Tools is not null)
            tools.AddRange((await client.ListToolsAsync(cancellationToken: cancellationToken)).Select(McpItem.FromTool));
        if (caps?.Resources is not null)
        {
            resources.AddRange((await client.ListResourcesAsync(cancellationToken: cancellationToken)).Select(McpItem.FromResource));
            try
            {
                resources.AddRange((await client.ListResourceTemplatesAsync(cancellationToken: cancellationToken)).Select(McpItem.FromTemplate));
            }
            catch (McpException)
            {
                // Some servers list resources but not templates.
            }
        }
        if (caps?.Prompts is not null)
            prompts.AddRange((await client.ListPromptsAsync(cancellationToken: cancellationToken)).Select(McpItem.FromPrompt));

        static List<McpItem> Sorted(List<McpItem> items) => [.. items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
        return new ServerItems(Sorted(tools), Sorted(resources), Sorted(prompts));
    }

    /// <summary>Calls a tool, reads a resource (or template) or gets a prompt.</summary>
    public static async Task<CallOutcome> RunAsync(McpClient client, McpItem item, JsonObject arguments, CancellationToken cancellationToken)
    {
        var callArguments = arguments.ToDictionary(
            kv => kv.Key,
            kv => (object?)(kv.Value is null ? null : JsonSerializer.SerializeToElement(kv.Value)));
        switch (item.Kind)
        {
            case ItemKind.Tool:
                var result = await client.CallToolAsync(item.Key, callArguments, cancellationToken: cancellationToken);
                return new CallOutcome(result.IsError == true ? ResultStatus.ToolError : ResultStatus.Success, FormatResult(result));
            case ItemKind.Resource:
                return new CallOutcome(ResultStatus.Success, FormatResource(await client.ReadResourceAsync(item.Key, cancellationToken: cancellationToken)));
            case ItemKind.ResourceTemplate:
                return new CallOutcome(ResultStatus.Success, FormatResource(await client.ReadResourceAsync(item.Key, callArguments, cancellationToken: cancellationToken)));
            default:
                return new CallOutcome(ResultStatus.Success, FormatPrompt(await client.GetPromptAsync(item.Key, callArguments, cancellationToken: cancellationToken)));
        }
    }

    /// <summary>Required arguments as empty values of their type, plus optional ones that have a default.</summary>
    public static JsonObject RawTemplate(JsonElement schema)
    {
        var template = new JsonObject();
        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            var required = schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
                ? req.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToHashSet()
                : [];
            foreach (var p in props.EnumerateObject())
            {
                if (p.Value.TryGetProperty("default", out var def))
                {
                    template[p.Name] = JsonNode.Parse(def.GetRawText());
                    continue;
                }
                if (!required.Contains(p.Name)) continue;
                string type = p.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "string";
                template[p.Name] = type switch
                {
                    "integer" or "number" => 0,
                    "boolean" => false,
                    "array" => new JsonArray(),
                    "object" => new JsonObject(),
                    _ => "",
                };
            }
        }
        return template;
    }

    /// <summary>Parses the Raw JSON box: an object, or empty for no arguments.</summary>
    public static bool TryParseArguments(string text, out JsonObject arguments, out string message)
    {
        arguments = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            message = "Empty: called without arguments";
            return true;
        }
        try
        {
            if (JsonNode.Parse(text) is JsonObject parsed)
            {
                arguments = parsed;
                message = parsed.Count == 0 ? "Valid JSON, no arguments" : parsed.Count == 1 ? "Valid JSON · 1 argument" : $"Valid JSON · {parsed.Count} arguments";
                return true;
            }
            message = "Arguments must be a JSON object: { … }";
        }
        catch (JsonException ex)
        {
            message = $"Not valid JSON ({JsonText.Describe(ex)})";
        }
        return false;
    }

    public static string FormatResult(CallToolResult result)
    {
        var parts = new List<string>();
        foreach (var block in result.Content)
            parts.Add(FormatBlock(block));
        if (parts.Count == 0 && result.StructuredContent is { } structured)
            parts.Add(JsonText.Format(structured));
        return parts.Count == 0 ? "(The tool returned no content.)" : string.Join("\n\n", parts);
    }

    public static string FormatResource(ReadResourceResult result)
    {
        bool several = result.Contents.Count > 1;
        var parts = result.Contents.Select(content =>
        {
            string body = content switch
            {
                TextResourceContents text => JsonText.TryFormat(text.Text, out var pretty) ? pretty : text.Text,
                BlobResourceContents blob => $"[binary {blob.MimeType ?? "data"}, {blob.DecodedData.Length:N0} bytes]",
                _ => content.ToString() ?? "",
            };
            return several ? $"── {content.Uri} ({content.MimeType ?? "unknown type"}) ──\n{body}" : body;
        }).ToList();
        return parts.Count == 0 ? "(The resource is empty.)" : string.Join("\n\n", parts);
    }

    public static string FormatPrompt(GetPromptResult result)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.Description))
            text.Append(result.Description.Trim()).Append("\n\n");
        foreach (var message in result.Messages)
            text.Append("── ").Append(message.Role.ToString().ToLowerInvariant()).Append(" ──\n").Append(FormatBlock(message.Content)).Append("\n\n");
        return text.Length == 0 ? "(The prompt has no messages.)" : text.ToString().TrimEnd();
    }

    private static string FormatBlock(ContentBlock block) => block switch
    {
        TextContentBlock text => JsonText.TryFormat(text.Text, out var pretty) ? pretty : text.Text,
        ImageContentBlock image => $"[image: {image.MimeType}, {image.DecodedData.Length:N0} bytes]",
        _ => DescribeBlock(block),
    };

    private static string DescribeBlock(ContentBlock block)
    {
        try
        {
            return JsonSerializer.Serialize(block, JsonText.Pretty);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return $"[{block.Type} content]";
        }
    }

    /// <summary>The current PATH plus the usual Homebrew, npm and pip folders (macOS and Linux).</summary>
    private static string ExtendedPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var extra in new[] { "/opt/homebrew/bin", "/usr/local/bin", Path.Combine(home, ".local", "bin"), Path.Combine(home, ".cargo", "bin"), "/usr/bin", "/bin" })
            if (!dirs.Contains(extra)) dirs.Add(extra);
        return string.Join(':', dirs);
    }

    private static string? FindExecutable(string command, string path)
    {
        if (command.Contains('/')) return null;
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, command);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
