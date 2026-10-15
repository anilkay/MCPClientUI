using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpClientUI;

/// <summary>
/// Builds one input row per property of a tool's JSON input schema and turns the
/// entered values back into call arguments.
/// </summary>
internal sealed class ArgumentEditor : TableLayoutPanel
{
    private readonly List<Field> fields = [];

    private sealed record Field(string Name, string Type, bool Required, Control Input);

    public ArgumentEditor()
    {
        AutoScroll = true;
        ColumnCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Padding = new Padding(4);
    }

    public void Clear()
    {
        Controls.Clear();
        RowStyles.Clear();
        fields.Clear();
        RowCount = 0;
    }

    public void Load(JsonElement schema)
    {
        SuspendLayout();
        Clear();

        var required = new HashSet<string>();
        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            foreach (var r in req.EnumerateArray())
                if (r.GetString() is { } s) required.Add(s);

        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
                AddRow(prop.Name, prop.Value, required.Contains(prop.Name));
        }

        if (fields.Count == 0)
        {
            var none = new Label { Text = "This tool takes no arguments.", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(none, 0, 0);
            SetColumnSpan(none, 2);
            RowCount++;
        }

        // Filler row so rows stay at the top.
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        RowCount++;
        ResumeLayout();
    }

    private void AddRow(string name, JsonElement prop, bool required)
    {
        string type = GetType(prop);
        string? description = prop.TryGetProperty("description", out var d) ? d.GetString() : null;

        Control input;
        if (prop.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            if (!required) combo.Items.Add("");
            foreach (var v in en.EnumerateArray())
                combo.Items.Add(v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText());
            combo.SelectedIndex = 0;
            input = combo;
        }
        else if (type == "boolean")
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            if (!required) combo.Items.Add("");
            combo.Items.AddRange(["true", "false"]);
            combo.SelectedIndex = 0;
            input = combo;
        }
        else
        {
            bool multiline = type is "object" or "array";
            input = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = multiline,
                Height = multiline ? 60 : 23,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                PlaceholderText = type switch
                {
                    "object" => "JSON object, e.g. {\"key\": \"value\"}",
                    "array" => "JSON array, e.g. [1, 2]",
                    _ => type,
                },
            };
            if (prop.TryGetProperty("default", out var def) && def.ValueKind != JsonValueKind.Null)
                input.Text = def.ValueKind == JsonValueKind.String ? def.GetString() : def.GetRawText();
        }

        var label = new Label
        {
            Text = required ? name + " *" : name,
            AutoSize = true,
            Margin = new Padding(3, 6, 8, 3),
            Font = required ? new Font(Font, FontStyle.Bold) : Font,
        };

        int row = RowCount;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(label, 0, row);
        Controls.Add(input, 1, row);
        RowCount++;

        if (!string.IsNullOrEmpty(description))
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(new Label
            {
                Text = description,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 8),
                MaximumSize = new Size(600, 0),
            }, 1, RowCount);
            RowCount++;
        }

        fields.Add(new Field(name, type, required, input));
    }

    private static string GetType(JsonElement prop)
    {
        if (!prop.TryGetProperty("type", out var t)) return "string";
        if (t.ValueKind == JsonValueKind.String) return t.GetString()!;
        // ["integer", "null"] style unions: take the first non-null type.
        if (t.ValueKind == JsonValueKind.Array)
            foreach (var v in t.EnumerateArray())
                if (v.GetString() is { } s && s != "null") return s;
        return "string";
    }

    /// <summary>Collects the entered values as a JSON object; throws FormatException on bad input.</summary>
    public JsonObject GetArguments()
    {
        var args = new JsonObject();
        foreach (var f in fields)
        {
            string text = f.Input.Text.Trim();
            if (text.Length == 0)
            {
                if (f.Required) throw new FormatException($"'{f.Name}' is required.");
                continue;
            }

            if (f.Type == "string")
            {
                args[f.Name] = f.Input.Text;
                continue;
            }

            try
            {
                args[f.Name] = JsonNode.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new FormatException($"'{f.Name}' is not valid {f.Type}: {ex.Message}");
            }
        }
        return args;
    }
}
