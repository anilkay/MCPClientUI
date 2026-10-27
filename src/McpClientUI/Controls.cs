using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace McpClientUI;

internal enum ButtonKind { Standard, Accent, Subtle }

/// <summary>Windows 11 style button: rounded, owner-painted, with an optional icon glyph.</summary>
internal sealed class FluentButton : Button
{
    private bool hover, pressed, keyboardFocus, isChecked;
    private ButtonKind kind;
    private string? glyph;

    public FluentButton(string text, ButtonKind kind = ButtonKind.Standard, string? glyph = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.kind = kind;
        this.glyph = glyph;
        Text = text;
        Font = Theme.Body;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind
    {
        get => kind;
        set { kind = value; PerformLayout(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph
    {
        get => glyph;
        set { glyph = value; Parent?.PerformLayout(); Invalidate(); }
    }

    /// <summary>Toggle look for subtle buttons (accent-tinted when on).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => isChecked;
        set { isChecked = value; Invalidate(); }
    }

    private const TextFormatFlags TextFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int height = LogicalToDeviceUnits(32);
        if (string.IsNullOrEmpty(Text))
            return new Size(height, height);
        int textWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFlags).Width;
        int glyphWidth = glyph is null ? 0 : LogicalToDeviceUnits(16) + LogicalToDeviceUnits(8);
        int padding = LogicalToDeviceUnits(kind == ButtonKind.Subtle ? 10 : 16);
        int minimum = kind == ButtonKind.Subtle ? 0 : LogicalToDeviceUnits(84);
        return new Size(Math.Max(minimum, padding * 2 + glyphWidth + textWidth), height);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    // Show the focus ring only when focus arrived by keyboard, as Windows 11 does.
    protected override void OnGotFocus(EventArgs e) { keyboardFocus = MouseButtons == MouseButtons.None; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { keyboardFocus = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        Color fill, text, border = Color.Empty;
        switch (kind)
        {
            case ButtonKind.Accent:
                fill = !Enabled ? Theme.AccentDisabled : pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
                text = Enabled ? Theme.TextOnAccent : Theme.TextOnAccentDisabled;
                break;
            case ButtonKind.Subtle:
                fill = isChecked ? Theme.AccentSubtle : !Enabled ? Color.Empty : pressed ? Theme.SubtlePressed : hover ? Theme.SubtleHover : Color.Empty;
                text = !Enabled ? Theme.DisabledText : isChecked ? Theme.AccentText : pressed ? Theme.SecondaryText : Theme.Text;
                break;
            default:
                fill = !Enabled ? Theme.ControlFill : pressed ? Theme.ControlPressed : hover ? Theme.ControlHover : Theme.ControlFill;
                text = !Enabled ? Theme.DisabledText : pressed ? Theme.SecondaryText : Theme.Text;
                border = Theme.ControlBorder;
                break;
        }

        float radius = LogicalToDeviceUnits(4);
        Theme.FillRounded(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), radius, fill, border);
        if (keyboardFocus && Focused)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = Theme.RoundedRect(new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), radius - 1);
            using var pen = new Pen(Theme.Text, 2);
            g.DrawPath(pen, ring);
        }

        bool hasText = !string.IsNullOrEmpty(Text);
        int glyphWidth = glyph is null ? 0 : LogicalToDeviceUnits(16);
        int gap = glyph is not null && hasText ? LogicalToDeviceUnits(8) : 0;
        int textWidth = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFlags).Width : 0;
        int x = (Width - glyphWidth - gap - textWidth) / 2;
        if (glyph is not null)
        {
            Theme.DrawGlyph(g, glyph, 10.5F, new Rectangle(x, 0, glyphWidth, Height), text);
            x += glyphWidth + gap;
        }
        if (hasText)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textWidth + 2, Height - 1), text,
                TextFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

/// <summary>Windows 11 style text box: rounded field with a bottom stroke that turns accent on focus.</summary>
internal sealed class InputBox : Control
{
    private readonly string? glyph;
    private bool hover, hasError;

    public InputBox(bool multiline = false, string? glyph = null, bool mono = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        this.glyph = glyph;
        TextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Multiline = multiline,
            Font = mono ? Theme.Mono : Theme.Body,
            ForeColor = Theme.Text,
            BackColor = Theme.ControlFill,
        };
        if (multiline)
        {
            TextBox.AcceptsReturn = true;
            TextBox.AcceptsTab = mono;
            TextBox.ScrollBars = ScrollBars.Vertical;
            TextBox.WordWrap = !mono;
        }
        Controls.Add(TextBox);
        TextBox.GotFocus += (_, _) => Refresh(updateBackground: true);
        TextBox.LostFocus += (_, _) => Refresh(updateBackground: true);
        TextBox.MouseEnter += (_, _) => { hover = true; Refresh(updateBackground: true); };
        TextBox.MouseLeave += (_, _) => { hover = ClientRectangle.Contains(PointToClient(MousePosition)); Refresh(updateBackground: true); };
        TextBox.HandleCreated += (_, _) => Theme.ApplyScrollBars(TextBox);
        Height = LogicalToDeviceUnits(multiline ? 96 : 32);
        Cursor = Cursors.IBeam;
    }

    public TextBox TextBox { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => TextBox.Text;
        set => TextBox.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => TextBox.PlaceholderText;
        set => TextBox.PlaceholderText = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasError
    {
        get => hasError;
        set { if (hasError != value) { hasError = value; Invalidate(); } }
    }

    private Color FillColor => TextBox.Focused ? Theme.InputFocused : hover ? Theme.ControlHover : Theme.ControlFill;

    private void Refresh(bool updateBackground)
    {
        if (updateBackground)
            TextBox.BackColor = FillColor;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Refresh(updateBackground: true); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e)
    {
        hover = TextBox.ClientRectangle.Contains(TextBox.PointToClient(MousePosition));
        Refresh(updateBackground: true);
        base.OnMouseLeave(e);
    }
    protected override void OnMouseDown(MouseEventArgs e) { TextBox.Focus(); base.OnMouseDown(e); }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int left = LogicalToDeviceUnits(glyph is null ? 11 : 36);
        if (TextBox.Multiline)
        {
            int top = LogicalToDeviceUnits(7);
            TextBox.SetBounds(left, top, Math.Max(1, Width - left - LogicalToDeviceUnits(3)), Math.Max(1, Height - top - LogicalToDeviceUnits(6)));
        }
        else
        {
            int height = TextBox.PreferredHeight;
            TextBox.SetBounds(left, (Height - height) / 2, Math.Max(1, Width - left - LogicalToDeviceUnits(10)), height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = LogicalToDeviceUnits(4);
        Theme.FillRounded(g, bounds, radius, FillColor, Theme.ControlBorder);

        bool focused = TextBox.Focused;
        Color line = hasError ? Theme.Danger : focused ? Theme.Accent : Theme.ControlStrongBorder;
        int thickness = hasError || focused ? LogicalToDeviceUnits(2) : 1;
        using (var path = Theme.RoundedRect(bounds, radius))
        using (var brush = new SolidBrush(line))
        {
            g.SetClip(path);
            g.FillRectangle(brush, 0, Height - 1 - thickness, Width, thickness + 1);
            g.ResetClip();
        }

        if (glyph is not null)
        {
            int glyphHeight = TextBox.Multiline ? LogicalToDeviceUnits(32) : Height;
            Theme.DrawGlyph(g, glyph, 10F, new Rectangle(LogicalToDeviceUnits(12), 0, LogicalToDeviceUnits(16), glyphHeight), Theme.TertiaryText);
        }
    }
}

/// <summary>A rounded card on the window background.</summary>
internal class Card : Panel
{
    private readonly int radius;

    public Card(int radius = 8)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.radius = radius;
        BackColor = Theme.Surface;
        // Keep children clear of the rounded corners.
        Padding = new Padding(LogicalToDeviceUnits(4));
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Border;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        Theme.FillRounded(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(radius), BackColor, BorderColor);
    }
}

/// <summary>An inset, slightly darker area for code and results.</summary>
internal sealed class CodePanel : Card
{
    public CodePanel(Control content) : base(radius: 6)
    {
        BackColor = Theme.SurfaceAlt;
        Padding = new Padding(LogicalToDeviceUnits(14), LogicalToDeviceUnits(10), LogicalToDeviceUnits(3), LogicalToDeviceUnits(3));
        content.Dock = DockStyle.Fill;
        content.BackColor = Theme.SurfaceAlt;
        Controls.Add(content);
    }
}

/// <summary>A row of mutually exclusive options (used for booleans and short enums).</summary>
internal sealed class SegmentedControl : Control
{
    private readonly string[] labels;
    private int selected, hover = -1;
    private bool keyboardFocus;

    public SegmentedControl(string[] labels, int selected)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        this.labels = labels;
        this.selected = selected;
        Font = Theme.Body;
        TabStop = true;
        Size = new Size(PreferredWidth, LogicalToDeviceUnits(32));
    }

    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value == selected) return;
            selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int SegmentWidth => labels.Max(l => TextRenderer.MeasureText(l, Theme.BodyStrong).Width) + LogicalToDeviceUnits(22);

    public int PreferredWidth => SegmentWidth * labels.Length + LogicalToDeviceUnits(6);

    private Rectangle SegmentBounds(int index)
    {
        int inset = LogicalToDeviceUnits(3);
        int width = SegmentWidth;
        return new Rectangle(inset + index * width, inset, width, Height - inset * 2);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < labels.Length; i++)
            if (SegmentBounds(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = HitTest(e.Location);
        if (index != hover) { hover = index; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        int index = HitTest(e.Location);
        if (index >= 0) SelectedIndex = index;
        base.OnMouseDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { keyboardFocus = MouseButtons == MouseButtons.None; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { keyboardFocus = false; Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, selected - 1);
        else if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(labels.Length - 1, selected + 1);
        else if (e.KeyCode == Keys.Space && selected < 0) SelectedIndex = 0;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var track = new RectangleF(0.5f, 0.5f, PreferredWidth - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, track, LogicalToDeviceUnits(5), Theme.TrackFill, Theme.ControlBorder);

        for (int i = 0; i < labels.Length; i++)
        {
            var r = SegmentBounds(i);
            bool isSelected = i == selected;
            if (isSelected)
            {
                Theme.FillRounded(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), LogicalToDeviceUnits(4), Theme.TrackSelected, Theme.ControlBorder);
                int pill = LogicalToDeviceUnits(14);
                Theme.FillRounded(g, new RectangleF(r.X + (r.Width - pill) / 2f, r.Bottom - LogicalToDeviceUnits(4), pill, LogicalToDeviceUnits(3)), LogicalToDeviceUnits(1.5f), Theme.Accent);
            }
            else if (i == hover)
            {
                Theme.FillRounded(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), LogicalToDeviceUnits(4), Theme.TrackHover);
            }
            var textBounds = new Rectangle(r.X, r.Y - LogicalToDeviceUnits(1), r.Width, r.Height);
            TextRenderer.DrawText(g, labels[i], isSelected ? Theme.BodyStrong : Theme.Body, textBounds,
                isSelected ? Theme.Text : Theme.SecondaryText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        if (keyboardFocus && Focused)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = Theme.RoundedRect(new RectangleF(1, 1, PreferredWidth - 2.5f, Height - 2.5f), LogicalToDeviceUnits(5));
            using var pen = new Pen(Theme.Text, 2);
            g.DrawPath(pen, ring);
        }
    }

    private int LogicalToDeviceUnits(float value) => (int)Math.Round(value * DeviceDpi / 96f);
}

/// <summary>Tab headers with an accent pill under the selected one (Windows 11 SelectorBar).</summary>
internal sealed class PivotHeader : Control
{
    private readonly string[] items;
    private int selected, hover = -1;
    private string? hint;
    private Control? action;

    public PivotHeader(params string[] items)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        this.items = items;
        TabStop = true;
        Height = LogicalToDeviceUnits(46);
    }

    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value == selected) return;
            selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Hint
    {
        get => hint;
        set { hint = value; Invalidate(); }
    }

    /// <summary>Left offset of the first item, in logical pixels.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Inset { get; set; } = 16;

    /// <summary>A control shown at the right end (e.g. a button); the hint moves to its left.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? Action
    {
        get => action;
        set
        {
            if (action is not null) Controls.Remove(action);
            action = value;
            if (value is not null) Controls.Add(value);
            PerformLayout();
            Invalidate();
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (action is null) return;
        var size = action.GetPreferredSize(Size.Empty);
        action.SetBounds(Width - LogicalToDeviceUnits(10) - size.Width, (Height - size.Height) / 2 - 1, size.Width, size.Height);
    }

    private Rectangle ItemBounds(int index)
    {
        int x = LogicalToDeviceUnits(Inset);
        for (int i = 0; i < index; i++)
            x += TextRenderer.MeasureText(items[i], Theme.BodyStrong).Width + LogicalToDeviceUnits(12);
        return new Rectangle(x, 0, TextRenderer.MeasureText(items[index], Theme.BodyStrong).Width, Height);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < items.Length; i++)
            if (Rectangle.Inflate(ItemBounds(i), LogicalToDeviceUnits(6), 0).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = HitTest(e.Location);
        if (index != hover) { hover = index; Cursor = index >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = HitTest(e.Location);
        if (index >= 0) SelectedIndex = index;
        base.OnMouseDown(e);
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, selected - 1);
        else if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(items.Length - 1, selected + 1);
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        for (int i = 0; i < items.Length; i++)
        {
            var r = ItemBounds(i);
            bool isSelected = i == selected;
            var color = isSelected || i == hover ? Theme.Text : Theme.SecondaryText;
            TextRenderer.DrawText(g, items[i], Theme.BodyStrong, new Rectangle(r.X, 0, r.Width, Height - LogicalToDeviceUnits(2)), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (isSelected)
            {
                int pill = Math.Min(r.Width, LogicalToDeviceUnits(22));
                Theme.FillRounded(g, new RectangleF(r.X + (r.Width - pill) / 2f, Height - LogicalToDeviceUnits(3), pill, LogicalToDeviceUnits(3)),
                    LogicalToDeviceUnits(1), Theme.Accent);
                if (Focused && ShowFocusCues)
                {
                    using var pen = new Pen(Theme.TertiaryText) { DashStyle = DashStyle.Dot };
                    g.DrawRectangle(pen, Rectangle.Inflate(new Rectangle(r.X, LogicalToDeviceUnits(10), r.Width, Height - LogicalToDeviceUnits(22)), LogicalToDeviceUnits(4), 0));
                }
            }
        }

        if (!string.IsNullOrEmpty(hint))
        {
            var last = ItemBounds(items.Length - 1);
            int left = last.Right + LogicalToDeviceUnits(24);
            int right = action is { Visible: true } ? action.Left - LogicalToDeviceUnits(10) : Width - LogicalToDeviceUnits(16);
            TextRenderer.DrawText(g, hint, Theme.Caption, new Rectangle(left, 0, Math.Max(0, right - left), Height - LogicalToDeviceUnits(2)),
                Theme.TertiaryText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}

internal enum ConnectionState { Disconnected, Connecting, Connected, Failed }

/// <summary>Connection status: a colored dot and a short text on a tinted pill.</summary>
internal sealed class StatusPill : Control
{
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 40 };
    private ConnectionState state;
    private double phase;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Font = Theme.Caption;
        pulse.Tick += (_, _) => { phase += 0.15; Invalidate(); };
        Set(ConnectionState.Disconnected, "Not connected");
    }

    public void Set(ConnectionState newState, string text)
    {
        state = newState;
        Text = text;
        pulse.Enabled = newState == ConnectionState.Connecting;
        Size = GetPreferredSize(Size.Empty);
        Parent?.PerformLayout();
        Invalidate();
    }

    private Color DotColor => state switch
    {
        ConnectionState.Connecting => Theme.Caution,
        ConnectionState.Connected => Theme.Success,
        ConnectionState.Failed => Theme.Danger,
        _ => Theme.TertiaryText,
    };

    public override Size GetPreferredSize(Size proposedSize) => new(
        TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + LogicalToDeviceUnits(38),
        LogicalToDeviceUnits(28));

    protected override void Dispose(bool disposing)
    {
        if (disposing) pulse.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var background = Parent?.BackColor ?? Theme.Window;
        g.Clear(background);
        var dot = DotColor;
        var fill = state == ConnectionState.Disconnected ? Theme.Blend(background, Theme.TertiaryText, 0.12) : Theme.Tint(dot, background);
        Theme.FillRounded(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), (Height - 1) / 2f, fill);

        if (state == ConnectionState.Connecting)
            dot = Theme.Blend(fill, dot, 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(phase)));
        int size = LogicalToDeviceUnits(8);
        Theme.FillRounded(g, new RectangleF(LogicalToDeviceUnits(12), (Height - size) / 2f, size, size), size / 2f, dot);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(LogicalToDeviceUnits(26), 0, Width - LogicalToDeviceUnits(26), Height - 1), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A thin indeterminate progress line.</summary>
internal sealed class ProgressLine : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private double position;
    private bool running;

    public ProgressLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Height = LogicalToDeviceUnits(3);
        timer.Tick += (_, _) =>
        {
            position += 0.012;
            if (position > 1.35) position = -0.35;
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Running
    {
        get => running;
        set
        {
            running = value;
            position = -0.35;
            timer.Enabled = value;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        if (!running) return;
        float width = Width * 0.35f;
        float x = (float)(position * Width);
        g.SetClip(new Rectangle(LogicalToDeviceUnits(16), 0, Width - LogicalToDeviceUnits(32), Height));
        Theme.FillRounded(g, new RectangleF(x, 0, width, Height), Height / 2f, Theme.Accent);
    }
}

/// <summary>A glyph and a line of colored text (validation and status messages).</summary>
internal sealed class InlineStatus : Control
{
    private string? glyph;
    private Color color;

    public InlineStatus()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Font = Theme.Caption;
        Height = LogicalToDeviceUnits(32);
    }

    public void Set(string? newGlyph, string text, Color newColor)
    {
        glyph = newGlyph;
        Text = text;
        color = newColor;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        int x = 0;
        if (glyph is not null)
        {
            Theme.DrawGlyph(g, glyph, 9F, new Rectangle(0, 0, LogicalToDeviceUnits(16), Height), color);
            x = LogicalToDeviceUnits(22);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>A card with a big icon, a heading and some text, shown when there is nothing else to show.</summary>
internal sealed class EmptyState : Card
{
    private string glyph = Glyphs.Plug, heading = "", body = "";
    private Color glyphColor = Theme.Accent;
    private string[] chips = [];

    public EmptyState()
    {
        Font = Theme.Body;
    }

    public void Show(string newGlyph, string newHeading, string newBody, Color newGlyphColor, params string[] newChips)
    {
        glyph = newGlyph;
        heading = newHeading;
        body = newBody;
        glyphColor = newGlyphColor;
        chips = newChips;
        Invalidate();
    }

    private const TextFormatFlags Wrapped = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int width = Math.Min(Width - LogicalToDeviceUnits(64), LogicalToDeviceUnits(560));
        if (width <= 0) return;
        int circle = LogicalToDeviceUnits(64);
        int headingHeight = TextRenderer.MeasureText(g, heading, Theme.Subtitle, new Size(width, int.MaxValue), Wrapped).Height;
        int bodyHeight = string.IsNullOrEmpty(body) ? 0 : TextRenderer.MeasureText(g, body, Theme.Body, new Size(width, int.MaxValue), Wrapped).Height;
        int chipsHeight = chips.Length == 0 ? 0 : LogicalToDeviceUnits(24);
        int total = circle + LogicalToDeviceUnits(20) + headingHeight + LogicalToDeviceUnits(8) + bodyHeight +
                    (chipsHeight > 0 ? LogicalToDeviceUnits(18) + chipsHeight : 0);
        int y = Math.Max(LogicalToDeviceUnits(24), (Height - total) / 2 - LogicalToDeviceUnits(12));
        int left = (Width - width) / 2;

        Theme.FillRounded(g, new RectangleF((Width - circle) / 2f, y, circle, circle), circle / 2f, Theme.Tint(glyphColor, Theme.Surface));
        Theme.DrawGlyph(g, glyph, 20F, new Rectangle((Width - circle) / 2, y, circle, circle), glyphColor);
        y += circle + LogicalToDeviceUnits(20);

        TextRenderer.DrawText(g, heading, Theme.Subtitle, new Rectangle(left, y, width, headingHeight), Theme.Text, Wrapped);
        y += headingHeight + LogicalToDeviceUnits(8);
        if (bodyHeight > 0)
        {
            TextRenderer.DrawText(g, body, Theme.Body, new Rectangle(left, y, width, bodyHeight), Theme.SecondaryText, Wrapped);
            y += bodyHeight;
        }

        if (chips.Length > 0)
        {
            y += LogicalToDeviceUnits(18);
            int gap = LogicalToDeviceUnits(6);
            int chipsWidth = chips.Sum(c => Theme.MeasureChip(this, c)) + gap * (chips.Length - 1);
            int x = (Width - chipsWidth) / 2;
            foreach (var chip in chips)
                x += Theme.DrawChip(g, this, x, y, chip, Theme.SecondaryText, Theme.NeutralSubtle) + gap;
        }
    }
}

/// <summary>The top bar: app icon and name on the left, URL box, connect button and status on the right.</summary>
internal sealed class HeaderBar : Panel
{
    private const string AppName = "MCP Client";
    private readonly Control url, extra, button, status;
    private readonly Icon logo;

    public HeaderBar(Control url, Control extra, Control button, Control status)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.url = url;
        this.extra = extra;
        logo = Theme.AppIcon(LogicalToDeviceUnits(32));
        this.button = button;
        this.status = status;
        BackColor = Theme.Window;
        Height = LogicalToDeviceUnits(68);
        Controls.AddRange([url, extra, button, status]);
    }

    private int TitleRight => LogicalToDeviceUnits(20 + 32 + 12) + TextRenderer.MeasureText(AppName, Theme.AppTitle).Width;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = LogicalToDeviceUnits(20);
        int middle = Height / 2;
        var statusSize = status.GetPreferredSize(Size.Empty);
        status.SetBounds(Width - pad - statusSize.Width, middle - statusSize.Height / 2, statusSize.Width, statusSize.Height);
        var buttonSize = button.GetPreferredSize(Size.Empty);
        button.SetBounds(status.Left - LogicalToDeviceUnits(16) - buttonSize.Width, middle - buttonSize.Height / 2, buttonSize.Width, buttonSize.Height);
        var extraSize = extra.GetPreferredSize(Size.Empty);
        extra.SetBounds(button.Left - LogicalToDeviceUnits(8) - extraSize.Width, middle - extraSize.Height / 2, extraSize.Width, extraSize.Height);
        int left = TitleRight + LogicalToDeviceUnits(40);
        int right = extra.Left - LogicalToDeviceUnits(8);
        url.SetBounds(left, middle - url.Height / 2, Math.Max(LogicalToDeviceUnits(160), right - left), url.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int pad = LogicalToDeviceUnits(20);
        int icon = LogicalToDeviceUnits(32);
        var iconBounds = new Rectangle(pad, (Height - icon) / 2, icon, icon);
        g.DrawIcon(logo, iconBounds);
        TextRenderer.DrawText(g, AppName, Theme.AppTitle, new Rectangle(iconBounds.Right + LogicalToDeviceUnits(12), 0, Width, Height - 1), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

/// <summary>A small all-caps section title with a count and an optional button on the right.</summary>
internal sealed class SectionHeader : Panel
{
    private string title;
    private string count = "";

    public SectionHeader(string title, params Control[] actions)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.title = title;
        Height = LogicalToDeviceUnits(40);
        Controls.AddRange(actions);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => title;
        set { title = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Count
    {
        get => count;
        set { count = value; Invalidate(); }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int right = Width;
        foreach (Control action in Controls)
        {
            if (!action.Visible) continue;
            var size = action.GetPreferredSize(Size.Empty);
            right -= size.Width;
            action.SetBounds(right, (Height - size.Height) / 2, size.Width, size.Height);
            right -= LogicalToDeviceUnits(2);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int x = LogicalToDeviceUnits(12);
        var titleSize = TextRenderer.MeasureText(g, title, Theme.CaptionStrong, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, title, Theme.CaptionStrong, new Rectangle(x, 0, titleSize.Width + 4, Height), Theme.SecondaryText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (count.Length > 0)
        {
            int chipX = x + titleSize.Width + LogicalToDeviceUnits(8);
            int chipHeight = LogicalToDeviceUnits(20);
            int chipWidth = TextRenderer.MeasureText(g, count, Theme.CaptionStrong, Size.Empty, TextFormatFlags.NoPadding).Width + LogicalToDeviceUnits(14);
            var chip = new RectangleF(chipX, (Height - chipHeight) / 2f, chipWidth, chipHeight);
            Theme.FillRounded(g, chip, chipHeight / 2f, Theme.NeutralSubtle);
            TextRenderer.DrawText(g, count, Theme.CaptionStrong, Rectangle.Round(chip), Theme.SecondaryText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>The connected server, shown above the tool list; click it to see the server overview.</summary>
internal sealed class ServerItem : Control
{
    private bool hover, selected;
    private string title = "", subtitle = "";

    public ServerItem()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Height = LogicalToDeviceUnits(60);
        Cursor = Cursors.Hand;
    }

    public void Set(string newTitle, string newSubtitle)
    {
        title = newTitle;
        subtitle = newSubtitle;
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set { selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var r = new Rectangle(0, LogicalToDeviceUnits(4), Width, Height - LogicalToDeviceUnits(8));
        if (selected || hover)
            Theme.FillRounded(g, r, LogicalToDeviceUnits(5), selected ? Theme.SubtleSelected : Theme.SubtleHover);
        if (selected)
        {
            int pill = LogicalToDeviceUnits(18);
            Theme.FillRounded(g, new RectangleF(r.X, r.Y + (r.Height - pill) / 2f, LogicalToDeviceUnits(3), pill), LogicalToDeviceUnits(1.5f), Theme.Accent);
        }

        int icon = LogicalToDeviceUnits(32);
        var iconBounds = new Rectangle(r.X + LogicalToDeviceUnits(12), r.Y + (r.Height - icon) / 2, icon, icon);
        Theme.FillRounded(g, iconBounds, LogicalToDeviceUnits(6), Theme.AccentSubtle);
        Theme.DrawGlyph(g, Glyphs.Globe, 11F, iconBounds, Theme.AccentText);

        int x = iconBounds.Right + LogicalToDeviceUnits(12);
        int w = r.Right - x - LogicalToDeviceUnits(8);
        TextRenderer.DrawText(g, title, Theme.BodyStrong, new Rectangle(x, r.Y + LogicalToDeviceUnits(7), w, LogicalToDeviceUnits(20)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, subtitle, Theme.Caption, new Rectangle(x, r.Y + LogicalToDeviceUnits(27), w, LogicalToDeviceUnits(18)), Theme.SecondaryText,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private int LogicalToDeviceUnits(float value) => (int)Math.Round(value * DeviceDpi / 96f);
}
