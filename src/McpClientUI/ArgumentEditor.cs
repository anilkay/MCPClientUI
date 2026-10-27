using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpClientUI;

/// <summary>
/// One argument (an <see cref="ArgumentField"/> from McpClientUI.Core): name with type and
/// "required" chips, an input that fits the JSON schema type, an inline validation error and the
/// description.
/// </summary>
internal sealed class FieldView : Control
{
    private const TextFormatFlags Wrapped = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    private readonly ArgumentField argument;
    private readonly InputBox? box;
    private readonly SegmentedControl? segmented;
    private string? error;

    public FieldView(ArgumentField argument)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        this.argument = argument;

        if (argument.UsesChoices)
        {
            segmented = new SegmentedControl([.. argument.ChoiceLabels], argument.DefaultChoice);
            Controls.Add(segmented);
            segmented.SelectedIndexChanged += (_, _) => SetError(null);
            return;
        }

        box = new InputBox(multiline: argument.IsMultiline, mono: argument.IsMultiline)
        {
            Placeholder = argument.Placeholder,
            Value = argument.DefaultText,
        };
        if (argument.Kind == FieldKind.Enum)
        {
            box.TextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            box.TextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            box.TextBox.AutoCompleteCustomSource.AddRange([.. argument.EnumLabels]);
        }
        Controls.Add(box);
        box.TextBox.TextChanged += (_, _) => SetError(null);
        box.TextBox.Leave += (_, _) =>
        {
            if (argument.Kind != FieldKind.Text && box.Value.Trim().Length > 0)
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

    /// <summary>Raised when Enter is pressed in a single-line input.</summary>
    public event EventHandler? SubmitRequested;

    public string PropertyName => argument.Name;

    private int HeaderHeight => LogicalToDeviceUnits(24);
    private int InputTop => HeaderHeight + LogicalToDeviceUnits(6);
    private int InputHeight => segmented?.Height ?? box!.Height;

    public int MeasureHeight(int width)
    {
        int height = InputTop + InputHeight;
        if (error is not null)
            height += LogicalToDeviceUnits(6) + MeasureText(error, width - LogicalToDeviceUnits(22));
        if (!string.IsNullOrEmpty(argument.Description))
            height += LogicalToDeviceUnits(6) + MeasureText(argument.Description, width);
        return height;
    }

    private static int MeasureText(string text, int width) =>
        TextRenderer.MeasureText(text, Theme.Caption, new Size(Math.Max(1, width), int.MaxValue), Wrapped).Height;

    /// <summary>Shows a value (null clears the field / selects "Not set").</summary>
    public void SetValue(JsonNode? node)
    {
        var (text, choice) = argument.Display(node);
        if (segmented is not null) segmented.SelectedIndex = choice;
        else box!.Value = text;
        SetError(null);
    }

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
        string? problem = argument.Read(box?.Value ?? "", segmented?.SelectedIndex ?? -1, out value, out present);
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

        int nameWidth = TextRenderer.MeasureText(g, argument.Name, Theme.BodyStrong, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        TextRenderer.DrawText(g, argument.Name, Theme.BodyStrong, new Rectangle(0, 0, nameWidth + 2, HeaderHeight), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        int x = nameWidth + LogicalToDeviceUnits(10);
        int chipY = (HeaderHeight - LogicalToDeviceUnits(20)) / 2;
        x += Theme.DrawChip(g, this, x, chipY, argument.TypeLabel, Theme.SecondaryText, Theme.NeutralSubtle, font: Theme.MonoSmall, height: 20) + LogicalToDeviceUnits(6);
        if (argument.Required)
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
        if (!string.IsNullOrEmpty(argument.Description))
        {
            y += LogicalToDeviceUnits(6);
            TextRenderer.DrawText(g, argument.Description, Theme.Caption, new Rectangle(0, y, Width, MeasureText(argument.Description, Width)), Theme.SecondaryText, Wrapped);
        }
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

        foreach (var argument in ArgumentField.FromSchema(schema))
        {
            var field = new FieldView(argument) { BackColor = Theme.Surface };
            field.SubmitRequested += (_, _) => SubmitRequested?.Invoke(this, EventArgs.Empty);
            fields.Add(field);
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

    /// <summary>Fills the form from an arguments object; false when it has keys the form can't show.</summary>
    public bool SetValues(JsonObject values)
    {
        foreach (var field in fields)
            field.SetValue(values.TryGetPropertyValue(field.PropertyName, out var value) ? value : null);
        PerformLayout();
        return values.All(kv => fields.Any(f => f.PropertyName == kv.Key));
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
