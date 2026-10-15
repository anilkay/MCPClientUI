using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace McpClientUI;

/// <summary>The selected tool's name, description and hints, with the Run and Cancel buttons.</summary>
internal sealed class ToolHeader : Card
{
    private readonly record struct Chip(string Text, Color Fore, Color Back, string? Glyph);

    private const TextFormatFlags DescriptionFlags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                                                     TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis;

    private readonly List<Chip> chips = [];
    private string heading = "", description = "";
    private bool sizing;

    public ToolHeader()
    {
        RunButton = new FluentButton("Run", ButtonKind.Accent, Glyphs.Play);
        CancelButton = new FluentButton("Cancel", ButtonKind.Standard, Glyphs.Cancel) { Visible = false };
        Controls.Add(RunButton);
        Controls.Add(CancelButton);
        CancelButton.VisibleChanged += (_, _) => { PerformLayout(); Invalidate(); };
    }

    public FluentButton RunButton { get; }
    public FluentButton CancelButton { get; }

    private int PadX => LogicalToDeviceUnits(24);
    private int PadY => LogicalToDeviceUnits(18);
    private int TitleHeight => TextRenderer.MeasureText("Ag", Theme.Title, Size.Empty, TextFormatFlags.NoPadding).Height;

    public void SetTool(McpClientTool tool)
    {
        var pt = tool.ProtocolTool;
        heading = string.IsNullOrWhiteSpace(pt.Title) ? pt.Name : pt.Title;
        description = string.IsNullOrWhiteSpace(pt.Description) ? "This tool has no description." : pt.Description.Trim();

        chips.Clear();
        if (heading != pt.Name)
            chips.Add(new Chip(pt.Name, Theme.SecondaryText, Theme.NeutralSubtle, Glyphs.Code));
        if (pt.Annotations is { } a)
        {
            if (a.ReadOnlyHint == true) chips.Add(new Chip("Read-only", Theme.Success, Theme.Tint(Theme.Success, Theme.Surface), Glyphs.Check));
            if (a.DestructiveHint == true && a.ReadOnlyHint != true) chips.Add(new Chip("Destructive", Theme.Danger, Theme.Tint(Theme.Danger, Theme.Surface), Glyphs.Warning));
            if (a.IdempotentHint == true) chips.Add(new Chip("Idempotent", Theme.SecondaryText, Theme.NeutralSubtle, Glyphs.Refresh));
            if (a.OpenWorldHint == true) chips.Add(new Chip("Open world", Theme.SecondaryText, Theme.NeutralSubtle, Glyphs.Globe));
        }

        var schema = pt.InputSchema;
        int count = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
            ? props.EnumerateObject().Count() : 0;
        int required = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
            ? req.GetArrayLength() : 0;
        string args = count == 0 ? "No arguments" : count == 1 ? "1 argument" : $"{count} arguments";
        if (required > 0) args += $" · {required} required";
        chips.Add(new Chip(args, Theme.SecondaryText, Theme.NeutralSubtle, Glyphs.Form));

        UpdateHeight();
        Invalidate();
    }

    private int MeasureHeight(int width)
    {
        int descriptionHeight = TextRenderer.MeasureText(description, Theme.Body, new Size(Math.Max(1, width - PadX * 2), int.MaxValue), DescriptionFlags).Height;
        descriptionHeight = Math.Min(descriptionHeight, LogicalToDeviceUnits(100));
        return PadY + TitleHeight + LogicalToDeviceUnits(6) + descriptionHeight + LogicalToDeviceUnits(14) + LogicalToDeviceUnits(24) + PadY;
    }

    private void UpdateHeight()
    {
        if (sizing || Width <= 0 || RunButton is null) return;
        sizing = true;
        Height = MeasureHeight(Width);
        sizing = false;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateHeight();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (RunButton is null) return; // Card's constructor lays out before ours has run.
        var run = RunButton.GetPreferredSize(Size.Empty);
        int y = PadY + (TitleHeight - run.Height) / 2;
        RunButton.SetBounds(Width - PadX - run.Width, y, run.Width, run.Height);
        var cancel = CancelButton.GetPreferredSize(Size.Empty);
        CancelButton.SetBounds(RunButton.Left - LogicalToDeviceUnits(8) - cancel.Width, y, cancel.Width, cancel.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int buttons = Width - (CancelButton.Visible ? CancelButton.Left : RunButton.Left);
        int titleHeight = TitleHeight;
        TextRenderer.DrawText(g, heading, Theme.Title, new Rectangle(PadX, PadY, Width - PadX * 2 - buttons - LogicalToDeviceUnits(16), titleHeight), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        int y = PadY + titleHeight + LogicalToDeviceUnits(6);
        int width = Width - PadX * 2;
        int descriptionHeight = Math.Min(TextRenderer.MeasureText(g, description, Theme.Body, new Size(width, int.MaxValue), DescriptionFlags).Height, LogicalToDeviceUnits(100));
        TextRenderer.DrawText(g, description, Theme.Body, new Rectangle(PadX, y, width, descriptionHeight), Theme.SecondaryText, DescriptionFlags);

        y += descriptionHeight + LogicalToDeviceUnits(14);
        int x = PadX;
        foreach (var chip in chips)
        {
            if (x + Theme.MeasureChip(this, chip.Text, chip.Glyph) > Width - PadX) break;
            x += Theme.DrawChip(g, this, x, y, chip.Text, chip.Fore, chip.Back, chip.Glyph) + LogicalToDeviceUnits(6);
        }
    }
}

internal enum ResultStatus { None, Running, Success, ToolError, Failed, Cancelled }

/// <summary>"Result" title with a status chip, timing, and Wrap / Copy buttons.</summary>
internal sealed class ResultHeader : Panel
{
    private ResultStatus status;
    private string meta = "";

    public ResultHeader()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        WrapButton = new FluentButton("Wrap", ButtonKind.Subtle, Glyphs.Wrap) { Checked = true };
        CopyButton = new FluentButton("Copy", ButtonKind.Subtle, Glyphs.Copy);
        Controls.Add(WrapButton);
        Controls.Add(CopyButton);
        Height = LogicalToDeviceUnits(52);
    }

    public FluentButton WrapButton { get; }
    public FluentButton CopyButton { get; }

    public void SetStatus(ResultStatus newStatus, string newMeta)
    {
        status = newStatus;
        meta = newMeta;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int y = (Height - LogicalToDeviceUnits(32)) / 2 + LogicalToDeviceUnits(2);
        var copy = CopyButton.GetPreferredSize(Size.Empty);
        CopyButton.SetBounds(Width - LogicalToDeviceUnits(12) - copy.Width, y, copy.Width, copy.Height);
        var wrap = WrapButton.GetPreferredSize(Size.Empty);
        WrapButton.SetBounds(CopyButton.Left - LogicalToDeviceUnits(4) - wrap.Width, y, wrap.Width, wrap.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int x = LogicalToDeviceUnits(20);
        int top = LogicalToDeviceUnits(2);
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        int titleWidth = TextRenderer.MeasureText(g, "Result", Theme.Subtitle, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, "Result", Theme.Subtitle, new Rectangle(x, top, titleWidth + 2, Height - top), Theme.Text, flags);
        x += titleWidth + LogicalToDeviceUnits(12);

        (string text, Color fore, Color back, string? glyph)? chip = status switch
        {
            ResultStatus.Running => ("Running", Theme.AccentText, Theme.AccentSubtle, Glyphs.Clock),
            ResultStatus.Success => ("Success", Theme.Success, Theme.Tint(Theme.Success, Theme.Surface), Glyphs.Check),
            ResultStatus.ToolError => ("Tool error", Theme.Danger, Theme.Tint(Theme.Danger, Theme.Surface), Glyphs.Error),
            ResultStatus.Failed => ("Failed", Theme.Danger, Theme.Tint(Theme.Danger, Theme.Surface), Glyphs.Error),
            ResultStatus.Cancelled => ("Cancelled", Theme.SecondaryText, Theme.NeutralSubtle, Glyphs.Cancel),
            _ => null,
        };
        if (chip is { } c)
        {
            int chipY = top + (Height - top - LogicalToDeviceUnits(24)) / 2;
            x += Theme.DrawChip(g, this, x, chipY, c.text, c.fore, c.back, c.glyph) + LogicalToDeviceUnits(10);
        }
        if (meta.Length > 0)
        {
            int right = WrapButton.Left - LogicalToDeviceUnits(8);
            TextRenderer.DrawText(g, meta, Theme.Caption, new Rectangle(x, top, Math.Max(0, right - x), Height - top), Theme.TertiaryText,
                flags | TextFormatFlags.EndEllipsis);
        }
    }
}
