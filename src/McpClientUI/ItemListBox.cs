using System.ComponentModel;

namespace McpClientUI;

/// <summary>A row in a sidebar list.</summary>
internal sealed record ListEntry(string Title, string Subtitle, object Tag, Color? Dot = null, string? Trailing = null)
{
    public override string ToString() => Title;
}

/// <summary>
/// Owner-painted sidebar list: a bold title, a gray second line, an optional status dot and
/// trailing text (e.g. a time), with the Windows 11 selection pill.
/// </summary>
internal sealed class ItemListBox : ListBox
{
    private int hoverIndex = -1;
    private string emptyText = "";

    public ItemListBox()
    {
        // UserPaint + OnPaint drawing every visible item avoids the flicker of a plain owner-drawn ListBox.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = LogicalToDeviceUnits(58);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText
    {
        get => emptyText;
        set { emptyText = value; Invalidate(); }
    }

    public void SetEntries(IEnumerable<ListEntry> entries, Func<ListEntry, bool>? select = null)
    {
        BeginUpdate();
        Items.Clear();
        int selected = -1;
        foreach (var entry in entries)
        {
            int index = Items.Add(entry);
            if (selected < 0 && select is not null && select(entry)) selected = index;
        }
        if (selected >= 0) SelectedIndex = selected;
        EndUpdate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyScrollBars(this);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        Invalidate();
        base.OnSelectedIndexChanged(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        UpdateHover(e.Location);
        base.OnMouseMove(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        UpdateHover(e.Location);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private void UpdateHover(Point location)
    {
        int index = IndexFromPoint(location);
        if (index >= 0 && !GetItemRectangle(index).Contains(location)) index = -1;
        if (index == hoverIndex) return;
        hoverIndex = index;
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        // Everything is drawn in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Items.Count == 0)
        {
            if (emptyText.Length > 0)
                TextRenderer.DrawText(g, emptyText, Theme.Caption, new Rectangle(LogicalToDeviceUnits(8), LogicalToDeviceUnits(16), Width - LogicalToDeviceUnits(16), LogicalToDeviceUnits(60)),
                    Theme.TertiaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }

        for (int i = Math.Max(0, TopIndex); i < Items.Count; i++)
        {
            var bounds = GetItemRectangle(i);
            if (bounds.Top > ClientSize.Height) break;
            if (bounds.IntersectsWith(e.ClipRectangle))
                DrawEntry(g, bounds, i);
        }
    }

    private const TextFormatFlags LineFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                              TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private void DrawEntry(Graphics g, Rectangle bounds, int index)
    {
        var entry = (ListEntry)Items[index];
        bool selected = index == SelectedIndex;
        var r = Rectangle.Inflate(bounds, 0, -LogicalToDeviceUnits(2));
        if (selected || index == hoverIndex)
            Theme.FillRounded(g, r, LogicalToDeviceUnits(5), selected ? Theme.SubtleSelected : Theme.SubtleHover);
        if (selected)
        {
            int pill = LogicalToDeviceUnits(18);
            Theme.FillRounded(g, new RectangleF(r.X, r.Y + (r.Height - pill) / 2f, LogicalToDeviceUnits(3), pill), 1.5f, Theme.Accent);
        }

        int x = r.X + LogicalToDeviceUnits(14);
        int right = r.Right - LogicalToDeviceUnits(10);
        int titleTop = r.Y + LogicalToDeviceUnits(9);
        if (entry.Dot is { } dot)
        {
            int size = LogicalToDeviceUnits(8);
            Theme.FillRounded(g, new RectangleF(x, titleTop + LogicalToDeviceUnits(6), size, size), size / 2f, dot);
            x += size + LogicalToDeviceUnits(8);
        }
        if (entry.Trailing is { Length: > 0 } trailing)
        {
            int width = TextRenderer.MeasureText(g, trailing, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, trailing, Theme.Caption, new Rectangle(right - width, titleTop + LogicalToDeviceUnits(2), width + 2, LogicalToDeviceUnits(18)),
                Theme.TertiaryText, LineFlags);
            right -= width + LogicalToDeviceUnits(8);
        }
        TextRenderer.DrawText(g, entry.Title, Theme.BodyStrong, new Rectangle(x, titleTop, right - x, LogicalToDeviceUnits(20)), Theme.Text, LineFlags);
        int subtitleX = r.X + LogicalToDeviceUnits(14);
        TextRenderer.DrawText(g, entry.Subtitle, Theme.Caption, new Rectangle(subtitleX, r.Y + LogicalToDeviceUnits(30), r.Right - subtitleX - LogicalToDeviceUnits(10), LogicalToDeviceUnits(18)),
            Theme.SecondaryText, LineFlags);
    }
}
