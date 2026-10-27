using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpClientUI;

public enum FieldKind { Text, Integer, Number, Boolean, Enum, Json }

/// <summary>
/// One argument of a tool, prompt or resource template, read from its JSON schema: what kind of
/// input it needs, its default, and how typed text (or a picked choice) becomes a JSON value.
/// Both UIs draw it their own way.
/// </summary>
public sealed class ArgumentField
{
    private readonly List<JsonElement> enumValues = [];

    private ArgumentField(string name, JsonElement schema, bool required)
    {
        Name = name;
        Required = required;
        Description = schema.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        string type = SchemaType(schema);
        JsonElement? defaultValue = schema.TryGetProperty("default", out var dv) && dv.ValueKind != JsonValueKind.Null ? dv : null;

        if (schema.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array && en.GetArrayLength() > 0)
        {
            Kind = FieldKind.Enum;
            TypeLabel = "enum";
            enumValues.AddRange(en.EnumerateArray().Select(v => v.Clone()));
            EnumLabels = [.. enumValues.Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText())];
            int defaultIndex = defaultValue is { } def ? enumValues.FindIndex(v => v.GetRawText() == def.GetRawText()) : -1;
            if (EnumLabels.Count <= 5 && EnumLabels.All(l => l.Length <= 16))
            {
                var labels = new List<string>(EnumLabels);
                var nodes = enumValues.Select(v => JsonNode.Parse(v.GetRawText())).ToList();
                if (!required)
                {
                    labels.Insert(0, "Not set");
                    nodes.Insert(0, null);
                }
                ChoiceLabels = labels;
                Choices = [.. nodes];
                DefaultChoice = defaultIndex >= 0 ? defaultIndex + (required ? 0 : 1) : required ? -1 : 0;
            }
            else
            {
                Placeholder = "One of: " + string.Join(", ", EnumLabels);
                if (defaultIndex >= 0) DefaultText = EnumLabels[defaultIndex];
            }
        }
        else if (type == "boolean")
        {
            Kind = FieldKind.Boolean;
            TypeLabel = type;
            Choices = required ? [true, false] : [null, true, false];
            ChoiceLabels = required ? ["true", "false"] : ["Not set", "true", "false"];
            DefaultChoice = defaultValue is { ValueKind: JsonValueKind.True } ? (required ? 0 : 1)
                : defaultValue is { ValueKind: JsonValueKind.False } ? (required ? 1 : 2)
                : required ? -1 : 0;
        }
        else
        {
            Kind = type switch
            {
                "integer" => FieldKind.Integer,
                "number" => FieldKind.Number,
                "object" or "array" => FieldKind.Json,
                _ => FieldKind.Text,
            };
            TypeLabel = type;
            Placeholder = Kind switch
            {
                FieldKind.Integer => "Whole number",
                FieldKind.Number => "Number",
                FieldKind.Json => type == "array" ? "JSON array, e.g. [1, 2]" : "JSON object, e.g. {\"key\": \"value\"}",
                _ => "Text",
            };
            if (defaultValue is { } def)
                DefaultText = def.ValueKind == JsonValueKind.String ? def.GetString()!
                    : Kind == FieldKind.Json ? JsonText.Format(def).ReplaceLineEndings(Environment.NewLine)
                    : def.GetRawText();
        }
    }

    public string Name { get; }
    public bool Required { get; }
    public string? Description { get; }
    public FieldKind Kind { get; }
    /// <summary>The schema type shown next to the name: string, integer, boolean, enum, object…</summary>
    public string TypeLabel { get; }
    public string Placeholder { get; } = "";

    /// <summary>True when the field is a small set of options (booleans, short enums) rather than a text box.</summary>
    public bool UsesChoices => Choices.Length > 0;
    public IReadOnlyList<string> ChoiceLabels { get; } = [];
    /// <summary>The JSON value of each choice; null means "Not set".</summary>
    public JsonNode?[] Choices { get; } = [];
    /// <summary>Selected choice at start; -1 for none (a required value with no default).</summary>
    public int DefaultChoice { get; } = -1;
    public string DefaultText { get; } = "";
    /// <summary>All enum values as text (for a long enum typed into a text box).</summary>
    public IReadOnlyList<string> EnumLabels { get; } = [];
    public bool IsMultiline => Kind == FieldKind.Json;

    /// <summary>One field per property of an object schema, in schema order.</summary>
    public static List<ArgumentField> FromSchema(JsonElement schema)
    {
        var fields = new List<ArgumentField>();
        if (schema.ValueKind != JsonValueKind.Object) return fields;
        var required = new HashSet<string>();
        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            foreach (var r in req.EnumerateArray())
                if (r.ValueKind == JsonValueKind.String) required.Add(r.GetString()!);
        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var prop in props.EnumerateObject())
                fields.Add(new ArgumentField(prop.Name, prop.Value, required.Contains(prop.Name)));
        return fields;
    }

    /// <summary>
    /// Turns the input into a JSON value. <paramref name="present"/> is false for an optional
    /// argument left empty, which is then left out of the call. Returns an error text, or null.
    /// </summary>
    public string? Read(string text, int choice, out JsonNode? value, out bool present)
    {
        value = null;
        present = false;

        if (UsesChoices)
        {
            if (choice < 0) return Required ? "Pick a value." : null;
            if (Choices[choice] is { } node)
            {
                value = node.DeepClone();
                present = true;
            }
            return null;
        }

        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return Required ? "This argument is required." : null;

        switch (Kind)
        {
            case FieldKind.Integer:
                if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)) return "Enter a whole number, like 42.";
                value = whole;
                break;
            case FieldKind.Number:
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                    return "Enter a number, like 3.5 (use a dot for decimals).";
                value = number;
                break;
            case FieldKind.Json:
                try
                {
                    var node = JsonNode.Parse(trimmed);
                    if (TypeLabel == "object" && node is not JsonObject) return "Enter a JSON object, like {\"key\": \"value\"}.";
                    if (TypeLabel == "array" && node is not JsonArray) return "Enter a JSON array, like [1, 2].";
                    value = node;
                }
                catch (JsonException ex)
                {
                    return $"That isn't valid JSON ({JsonText.Describe(ex)}).";
                }
                break;
            case FieldKind.Enum:
                int match = EnumLabels.ToList().IndexOf(trimmed);
                if (match < 0) return "Must be one of: " + string.Join(", ", EnumLabels) + ".";
                value = JsonNode.Parse(enumValues[match].GetRawText());
                break;
            default:
                // Strings are sent exactly as typed.
                value = text;
                break;
        }
        present = true;
        return null;
    }

    /// <summary>What to show for a value (null clears the field / selects "Not set").</summary>
    public (string Text, int Choice) Display(JsonNode? node)
    {
        if (UsesChoices)
        {
            string? wanted = node?.ToJsonString();
            int index = Array.FindIndex(Choices, c => c?.ToJsonString() == wanted);
            return ("", index >= 0 ? index : Required ? -1 : 0);
        }
        string text = node switch
        {
            null => "",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ when Kind == FieldKind.Json => node.ToJsonString(JsonText.Pretty).ReplaceLineEndings(Environment.NewLine),
            _ => node.ToJsonString(),
        };
        return (text, -1);
    }

    private static string SchemaType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var t)) return "string";
        if (t.ValueKind == JsonValueKind.String) return t.GetString()!;
        // ["integer", "null"] style unions: take the first non-null type.
        if (t.ValueKind == JsonValueKind.Array)
            foreach (var v in t.EnumerateArray())
                if (v.ValueKind == JsonValueKind.String && v.GetString() is { } s && s != "null") return s;
        return "string";
    }
}
