namespace McpClientUI;

/// <summary>
/// Headers ("Name: value" lines plus a Bearer token) and environment variables ("NAME=value"
/// lines) as typed in the Headers / Environment dialog.
/// </summary>
public static class SecretLines
{
    private const string Bearer = "Bearer ";

    /// <summary>Splits stored headers into the Bearer token and the other lines.</summary>
    public static (string Token, string Lines) SplitHeaders(IReadOnlyDictionary<string, string> headers)
    {
        string token = "";
        var lines = new List<string>();
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && value.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
                token = value[Bearer.Length..];
            else
                lines.Add($"{name}: {value}");
        }
        return (token, string.Join(Environment.NewLine, lines));
    }

    /// <summary>Returns an error text, or null with the parsed headers.</summary>
    public static string? ParseHeaders(string token, string lines, out Dictionary<string, string> headers)
    {
        headers = new(StringComparer.OrdinalIgnoreCase);
        int lineNumber = 0;
        foreach (var line in lines.Split('\n'))
        {
            lineNumber++;
            string text = line.Trim();
            if (text.Length == 0) continue;
            int colon = text.IndexOf(':');
            string name = colon > 0 ? text[..colon].Trim() : "";
            if (name.Length == 0 || name.Any(c => c <= ' ' || c >= 127 || "()<>@,;:\\\"/[]?={}".Contains(c)))
                return $"Line {lineNumber} should look like Name: value.";
            headers[name] = text[(colon + 1)..].Trim();
        }

        string tokenText = token.Trim();
        if (tokenText.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
            tokenText = tokenText[Bearer.Length..].Trim();
        if (tokenText.Length > 0)
        {
            if (headers.ContainsKey("Authorization"))
                return "Use either the Bearer token or an Authorization line, not both.";
            headers["Authorization"] = Bearer + tokenText;
        }
        return null;
    }

    public static string FormatEnvironment(IReadOnlyDictionary<string, string> variables) =>
        string.Join(Environment.NewLine, variables.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>Returns an error text, or null with the parsed variables.</summary>
    public static string? ParseEnvironment(string lines, out Dictionary<string, string> variables)
    {
        variables = new(StringComparer.OrdinalIgnoreCase);
        int lineNumber = 0;
        foreach (var line in lines.Split('\n'))
        {
            lineNumber++;
            string text = line.Trim();
            if (text.Length == 0) continue;
            int equals = text.IndexOf('=');
            string name = equals > 0 ? text[..equals].Trim() : "";
            if (name.Length == 0 || name.Any(char.IsWhiteSpace))
                return $"Line {lineNumber} should look like NAME=value.";
            variables[name] = text[(equals + 1)..];
        }
        return null;
    }
}
