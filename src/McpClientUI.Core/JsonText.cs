using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpClientUI;

/// <summary>Pretty-printing and error text for JSON.</summary>
public static class JsonText
{
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Pretty-prints a JSON object or array; false for anything else.</summary>
    public static bool TryFormat(string text, out string pretty)
    {
        pretty = text;
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            return false;
        try
        {
            var node = JsonNode.Parse(text);
            if (node is null) return false;
            pretty = node.ToJsonString(Pretty);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Format(JsonElement element) => JsonSerializer.Serialize(element, Pretty);

    /// <summary>Where a parse error is, in words ("line 2, position 5").</summary>
    public static string Describe(JsonException ex) => ex.LineNumber is long line
        ? $"line {line + 1}, position {(ex.BytePositionInLine ?? 0) + 1}"
        : "check the syntax";
}
