using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpClientUI;

internal enum FieldKind { Text, Integer, Number, Boolean, Enum, Json }

/// <summary>
/// One tool argument: name with type and "required" chips, an input that fits the JSON
/// schema type, an inline validation error and the description.
/// </summary>
internal sealed class FieldView : Control
{
    private const TextFormatFlags Wrapped = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    private readonly string? description;
    private readonly InputBox? box;
    private readonly SegmentedControl? segmented;
    private readonly JsonNode?[] choices = [];
    private readonly List<string> enumLabels = [];
    private readonly List<JsonElement> enumValues = [];
    private string? error;

    public FieldView(string name, JsonElement schema, bool required)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        PropertyName = name;
        Required = required;
        description = schema.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        string type = SchemaType(schema);
        JsonElement? defaultValue = schema.TryGetProperty("default", out var dv) && dv.ValueKind != JsonValueKind.Null ? dv : null;

        if (schema.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array && en.GetArrayLength() > 0)
        {
            Kind = FieldKind.Enum;
            TypeLabel = "enum";
            enumValues.AddRange(en.EnumerateArray().Select(v => v.Clone()));
            enumLabels.AddRange(enumValues.Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()));
            int defaultIndex = defaultValue is { } def ? enumValues.FindIndex(v => v.GetRawText() == def.GetRawText()) : -1;
            if (enumLabels.Count <= 5 && enumLabels.All(l => l.Length <= 16))
            {
                var labels = new List<string>(enumLabels);
                var nodes = enumValues.Select(v => JsonNode.Parse(v.GetRawText())).ToList();
                if (!required)
                {
                    labels.Insert(0, "Not set");
                    nodes.Insert(0, null);
                }
                choices = [.. nodes];
                int selected = defaultIndex >= 0 ? defaultIndex + (required ? 0 : 1) : required ? -1 : 0;
                segmented = new SegmentedControl([.. labels], selected);
            }
            else
            {
                box = new InputBox { Placeholder = "One of: " + string.Join(", ", enumLabels) };
                box.TextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.TextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.TextBox.AutoCompleteCustomSource.AddRange([.. enumLabels]);
                if (defaultIndex >= 0) box.Value = enumLabels[defaultIndex];
            }
        }
        else if (type == "boolean")
        {
            Kind = FieldKind.Boolean;
            TypeLabel = type;
            choices = required ? [true, false] : [null, true, false];
            string[] labels = required ? ["true", "false"] : ["Not set", "true", "false"];
            int selected = defaultValue is { ValueKind: JsonValueKind.True } ? (required ? 0 : 1)
                : defaultValue is { ValueKind: JsonValueKind.False } ? (required ? 1 : 2)
                : required ? -1 : 0;
            segmented = new SegmentedControl(labels, selected);
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
            box = new InputBox(multiline: Kind == FieldKind.Json, mono: Kind == FieldKind.Json)
            {
                Placeholder = Kind switch
                {
                    FieldKind.Integer => "Whole number",
                    FieldKind.Number => "Number",
                    FieldKind.Json => type == "array" ? "JSON array, e.g. [1, 2]" : "JSON object, e.g. {\"key\": \"value\"}",
                    _ => "Text",
                },
            };
            if (defaultValue is { } def)
                box.Value = def.ValueKind == JsonValueKind.String ? def.GetString()!
                    : Kind == FieldKind.Json ? JsonText.Format(def).ReplaceLineEndings("\r\n")
                    : def.GetRawText();
        }

        if (box is not null)
        {
            Controls.Add(box);
            box.TextBox.TextChanged += (_, _) => SetError(null);
            box.TextBox.Leave += (_, _) =>
            {
                if (Kind != FieldKind.Text && box.Value.Trim().Length > 0)
                    TryGetValue(showErrors: true, out _, out _);
            };
            if (!box.TextBox.Multiline)
            {
                box.TextBox.KeyDown += (_, e) =>
                {
                    if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
                    e.SuppressKeyPress = true;
                    SubmitRequested?.Invoke(this, EventArgs.Empty);
                };
            }
        }
        else
        {
            Controls.Add(segmented!);
            segmented!.SelectedIndexChanged += (_, _) => SetError(null);
        }
    }

    /// <summary>Raised when Enter is pressed in a single-line input.</summary>
    public event EventHandler? SubmitRequested;

    public string PropertyName { get; }
    public bool Required { get; }
    public FieldKind Kind { get; }
    public string TypeLabel { get; }

    private int HeaderHeight => LogicalToDeviceUnits(24);
    private int InputTop => HeaderHeight + LogicalToDeviceUnits(6);
    private int InputHeight => segmented?.Height ?? box!.Height;

    public int MeasureHeight(int width)
    {
        int height = InputTop + InputHeight;
        if (error is not null)
            height += LogicalToDeviceUnits(6) + MeasureText(error, width - LogicalToDeviceUnits(22));
        if (!string.IsNullOrEmpty(description))
            height += LogicalToDeviceUnits(6) + MeasureText(description, width);
        return height;
    }

    private static int MeasureText(string text, int width) =>
        TextRenderer.MeasureText(text, Theme.Caption, new Size(Math.Max(1, width), int.MaxValue), Wrapped).Height;

    public void FocusInput()
    {
        if (box is not null) box.TextBox.Focus();
        else segmented!.Focus();
    }

    /// <summary>
    /// Reads and checks the value. <paramref name="present"/> is false for an optional
    /// argument left empty, which is then left out of the call.
    /// </summary>
    public bool TryGetValue(bool showErrors, out JsonNode? value, out bool present)
    {
        value = null;
        present = false;
        string? problem = null;

        if (segmented is not null)
        {
            int index = segmented.SelectedIndex;
            if (index < 0)
            {
                if (Required) problem = "Pick a value.";
            }
            else if (choices[index] is { } node)
            {
                value = node.DeepClone();
                present = true;
            }
        }
        else
        {
            string raw = box!.Value;
            string text = raw.Trim();
            if (text.Length == 0)
            {
                if (Required) problem = "This argument is required.";
            }
            else
            {
                switch (Kind)
                {
                    case FieldKind.Integer:
                        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)) { value = whole; present = true; }
                        else problem = "Enter a whole number, like 42.";
                        break;
                    case FieldKind.Number:
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) { value = number; present = true; }
                        else problem = "Enter a number, like 3.5 (use a dot for decimals).";
                        break;
                    case FieldKind.Json:
                        try
                        {
                            var node = JsonNode.Parse(text);
                            if (TypeLabel == "object" && node is not JsonObject) problem = "Enter a JSON object, like {\"key\": \"value\"}.";
                            else if (TypeLabel == "array" && node is not JsonArray) problem = "Enter a JSON array, like [1, 2].";
                            else { value = node; present = true; }
                        }
                        catch (JsonException ex)
                        {
                            problem = $"That isn't valid JSON ({JsonText.Describe(ex)}).";
                        }
                        break;
                    case FieldKind.Enum:
                        int match = enumLabels.IndexOf(text);
                        if (match >= 0) { value = JsonNode.Parse(enumValues[match].GetRawText()); present = true; }
                        else problem = "Must be one of: " + string.Join(", ", enumLabels) + ".";
                        break;
                    default:
                        // Strings are sent exactly as typed.
                        value = raw;
                        present = true;
                        break;
                }
            }
        }

        if (showErrors) SetError(problem);
        return problem is null;
    }

    private void SetError(string? problem)
    {
        if (error == problem) return;
        error = problem;
        if (box is not null) box.HasError = problem is not null;
        Parent?.PerformLayout();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (segmented is not null)
            segmented.SetBounds(0, InputTop, segmented.PreferredWidth, segmented.Height);
        else
            box!.SetBounds(0, InputTop, Width, box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        int nameWidth = TextRenderer.MeasureText(g, PropertyName, Theme.BodyStrong, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        TextRenderer.DrawText(g, PropertyName, Theme.BodyStrong, new Rectangle(0, 0, nameWidth + 2, HeaderHeight), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        int x = nameWidth + LogicalToDeviceUnits(10);
        int chipY = (HeaderHeight - LogicalToDeviceUnits(20)) / 2;
        x += Theme.DrawChip(g, this, x, chipY, TypeLabel, Theme.SecondaryText, Theme.NeutralSubtle, font: Theme.MonoSmall, height: 20) + LogicalToDeviceUnits(6);
        if (Required)
            Theme.DrawChip(g, this, x, chipY, "required", Theme.AccentText, Theme.AccentSubtle, height: 20);

        int y = InputTop + InputHeight;
        if (error is not null)
        {
            y += LogicalToDeviceUnits(6);
            int height = MeasureText(error, Width - LogicalToDeviceUnits(22));
            Theme.DrawGlyph(g, Glyphs.Error, 9F, new Rectangle(0, y, LogicalToDeviceUnits(16), LogicalToDeviceUnits(18)), Theme.Danger);
            TextRenderer.DrawText(g, error, Theme.Caption, new Rectangle(LogicalToDeviceUnits(22), y, Width - LogicalToDeviceUnits(22), height), Theme.Danger, Wrapped);
            y += height;
        }
        if (!string.IsNullOrEmpty(description))
        {
            y += LogicalToDeviceUnits(6);
            TextRenderer.DrawText(g, description, Theme.Caption, new Rectangle(0, y, Width, MeasureText(description, Width)), Theme.SecondaryText, Wrapped);
        }
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

/// <summary>A scrollable form with one <see cref="FieldView"/> per property of a tool's input schema.</summary>
internal sealed class ArgumentEditor : Panel
{
    private readonly List<FieldView> fields = [];
    private bool hasSchema, layingOut;

    public ArgumentEditor()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        BackColor = Theme.Surface;
    }

    /// <summary>Raised when Enter is pressed in a single-line input.</summary>
    public event EventHandler? SubmitRequested;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyScrollBars(this);
    }

    public void Clear()
    {
        hasSchema = false;
        RemoveFields();
        Invalidate();
    }

    public void Load(JsonElement schema)
    {
        RemoveFields();
        hasSchema = true;
        AutoScrollPosition = Point.Empty;

        var required = new HashSet<string>();
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            foreach (var r in req.EnumerateArray())
                if (r.ValueKind == JsonValueKind.String) required.Add(r.GetString()!);

        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                var field = new FieldView(prop.Name, prop.Value, required.Contains(prop.Name)) { BackColor = Theme.Surface };
                field.SubmitRequested += (_, _) => SubmitRequested?.Invoke(this, EventArgs.Empty);
                fields.Add(field);
            }
        }

        SuspendLayout();
        Controls.AddRange([.. fields]);
        ResumeLayout(true);
        Invalidate();
    }

    private void RemoveFields()
    {
        SuspendLayout();
        foreach (var field in fields)
        {
            Controls.Remove(field);
            field.Dispose();
        }
        fields.Clear();
        AutoScrollMinSize = Size.Empty;
        ResumeLayout(false);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (layingOut) return;
        layingOut = true;
        try
        {
            int padX = LogicalToDeviceUnits(20), top = LogicalToDeviceUnits(16), gap = LogicalToDeviceUnits(18);
            int width = Math.Max(LogicalToDeviceUnits(160), Math.Min(ClientSize.Width - padX * 2, LogicalToDeviceUnits(720)));
            int y = top;
            foreach (var field in fields)
            {
                int height = field.MeasureHeight(width);
                field.SetBounds(padX + AutoScrollPosition.X, y + AutoScrollPosition.Y, width, height);
                y += height + gap;
            }
            AutoScrollMinSize = fields.Count == 0 ? Size.Empty : new Size(0, y - gap + top);
        }
        finally
        {
            layingOut = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!hasSchema || fields.Count > 0) return;
        var g = e.Graphics;
        int x = LogicalToDeviceUnits(20), y = LogicalToDeviceUnits(20);
        Theme.DrawGlyph(g, Glyphs.Info, 11F, new Rectangle(x, y, LogicalToDeviceUnits(18), LogicalToDeviceUnits(22)), Theme.AccentText);
        x += LogicalToDeviceUnits(28);
        TextRenderer.DrawText(g, "This tool takes no arguments.", Theme.BodyStrong, new Rectangle(x, y, Width - x, LogicalToDeviceUnits(22)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Press Run, or Ctrl+Enter, to call it.", Theme.Caption, new Rectangle(x, y + LogicalToDeviceUnits(24), Width - x, LogicalToDeviceUnits(20)),
            Theme.SecondaryText, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    /// <summary>Validates every field and shows errors inline; on failure focuses the first bad one.</summary>
    public bool TryGetArguments(out JsonObject arguments)
    {
        arguments = [];
        FieldView? firstInvalid = null;
        foreach (var field in fields)
        {
            if (field.TryGetValue(showErrors: true, out var value, out var present))
            {
                if (present) arguments[field.PropertyName] = value;
            }
            else
            {
                firstInvalid ??= field;
            }
        }
        PerformLayout();
        if (firstInvalid is null) return true;
        ScrollControlIntoView(firstInvalid);
        firstInvalid.FocusInput();
        return false;
    }

    /// <summary>The values that are filled in and valid, without showing errors.</summary>
    public JsonObject GetFilledArguments()
    {
        var arguments = new JsonObject();
        foreach (var field in fields)
            if (field.TryGetValue(showErrors: false, out var value, out var present) && present)
                arguments[field.PropertyName] = value;
        return arguments;
    }

    public void FocusFirst()
    {
        if (fields.Count > 0) fields[0].FocusInput();
    }

    public bool HasFields => fields.Count > 0;

    /// <summary>The height the form needs to show every field without scrolling.</summary>
    public int MeasureContentHeight(int width)
    {
        if (fields.Count == 0) return LogicalToDeviceUnits(84);
        int padX = LogicalToDeviceUnits(20), top = LogicalToDeviceUnits(16), gap = LogicalToDeviceUnits(18);
        int fieldWidth = Math.Max(LogicalToDeviceUnits(160), Math.Min(width - padX * 2, LogicalToDeviceUnits(720)));
        return top * 2 + fields.Sum(f => f.MeasureHeight(fieldWidth)) + gap * (fields.Count - 1);
    }
}
