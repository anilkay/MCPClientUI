using System.ComponentModel;
using ModelContextProtocol.Client;

namespace McpClientUI;

/// <summary>A list entry; <see cref="ToString"/> gives the tool name for keyboard search and accessibility.</summary>
internal sealed record ToolItem(McpClientTool Tool)
{
    public override string ToString() => Tool.Name;
}

/// <summary>Owner-painted tool list: name and first line of the description, Windows 11 selection pill.</summary>
internal sealed class ToolListBox : ListBox
{
    private int hoverIndex = -1;
    private string emptyText = "";

    public ToolListBox()
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
                TextRenderer.DrawText(g, emptyText, Theme.Caption, new Rectangle(0, LogicalToDeviceUnits(16), Width, LogicalToDeviceUnits(40)), Theme.TertiaryText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }

        for (int i = Math.Max(0, TopIndex); i < Items.Count; i++)
        {
            var bounds = GetItemRectangle(i);
            if (bounds.Top > ClientSize.Height) break;
            if (bounds.IntersectsWith(e.ClipRectangle))
                DrawTool(g, bounds, i);
        }
    }

    private const TextFormatFlags LineFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                              TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private void DrawTool(Graphics g, Rectangle bounds, int index)
    {
        var tool = ((ToolItem)Items[index]).Tool;
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
        int width = r.Right - x - LogicalToDeviceUnits(10);
        TextRenderer.DrawText(g, tool.Name, Theme.BodyStrong, new Rectangle(x, r.Y + LogicalToDeviceUnits(9), width, LogicalToDeviceUnits(20)),
            Theme.Text, LineFlags);
        TextRenderer.DrawText(g, FirstLine(tool.Description), Theme.Caption, new Rectangle(x, r.Y + LogicalToDeviceUnits(30), width, LogicalToDeviceUnits(18)),
            Theme.SecondaryText, LineFlags);
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "No description";
        text = text.Trim();
        int end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end];
    }
}
