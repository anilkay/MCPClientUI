using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace McpClientUI;

/// <summary>
/// Windows 11 (Fluent) style palette, fonts and drawing helpers. Follows the Windows
/// light/dark setting (via <see cref="Application.SetColorMode"/>) and accent color.
/// </summary>
internal static class Theme
{
    private static readonly HashSet<string> InstalledFonts = LoadInstalledFonts();
    private static readonly Dictionary<float, Font> IconFonts = [];
    private static readonly string IconFamily = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
    private static readonly string TextFamily = Pick("Segoe UI Variable Text", "Segoe UI");
    private static readonly string StrongFamily = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
    private static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas", "Courier New");

    public static Font Body { get; } = new(TextFamily, 10F);
    public static Font BodyStrong { get; } = new(StrongFamily, 10F);
    public static Font Caption { get; } = new(TextFamily, 9F);
    public static Font CaptionStrong { get; } = new(StrongFamily, 8.5F);
    public static Font Subtitle { get; } = new(StrongFamily, 12F);
    public static Font Title { get; } = new(StrongFamily, 17F);
    public static Font AppTitle { get; } = new(StrongFamily, 11F);
    public static Font Mono { get; } = new(MonoFamily, 9.75F);
    public static Font MonoSmall { get; } = new(MonoFamily, 8.25F);

    public static bool IsDark { get; private set; }

    public static Color Window { get; private set; }
    public static Color Surface { get; private set; }
    public static Color SurfaceAlt { get; private set; }
    public static Color Border { get; private set; }

    public static Color Text { get; private set; }
    public static Color SecondaryText { get; private set; }
    public static Color TertiaryText { get; private set; }
    public static Color DisabledText { get; private set; }

    public static Color ControlFill { get; private set; }
    public static Color ControlHover { get; private set; }
    public static Color ControlPressed { get; private set; }
    public static Color ControlBorder { get; private set; }
    public static Color ControlStrongBorder { get; private set; }
    public static Color InputFocused { get; private set; }

    public static Color SubtleHover { get; private set; }
    public static Color SubtlePressed { get; private set; }
    public static Color SubtleSelected { get; private set; }
    public static Color TrackFill { get; private set; }
    public static Color TrackHover { get; private set; }
    public static Color TrackSelected { get; private set; }

    public static Color BaseAccent { get; private set; }
    public static Color Accent { get; private set; }
    public static Color AccentHover { get; private set; }
    public static Color AccentPressed { get; private set; }
    public static Color AccentDisabled { get; private set; }
    public static Color AccentText { get; private set; }
    public static Color AccentSubtle { get; private set; }
    public static Color TextOnAccent { get; private set; }
    public static Color TextOnAccentDisabled { get; private set; }

    public static Color Success { get; private set; }
    public static Color Danger { get; private set; }
    public static Color Caution { get; private set; }
    public static Color NeutralSubtle { get; private set; }

    public static Color JsonKey { get; private set; }
    public static Color JsonString { get; private set; }
    public static Color JsonNumber { get; private set; }
    public static Color JsonLiteral { get; private set; }
    public static Color JsonPunctuation { get; private set; }

    public static void Initialize()
    {
        IsDark = Application.IsDarkModeEnabled;
        if (IsDark)
        {
            Window = Hex(0x202020);
            Surface = Hex(0x2B2B2B);
            SurfaceAlt = Hex(0x1F1F1F);
            Border = Hex(0x3A3A3A);
            Text = Hex(0xFFFFFF);
            SecondaryText = Hex(0xC5C5C5);
            TertiaryText = Hex(0x9E9E9E);
            DisabledText = Hex(0x787878);
            ControlFill = Hex(0x333333);
            ControlHover = Hex(0x3A3A3A);
            ControlPressed = Hex(0x2C2C2C);
            ControlBorder = Hex(0x444444);
            ControlStrongBorder = Hex(0x9A9A9A);
            InputFocused = Hex(0x1F1F1F);
            SubtleHover = Hex(0x333333);
            SubtlePressed = Hex(0x2F2F2F);
            SubtleSelected = Hex(0x383838);
            TrackFill = Hex(0x242424);
            TrackHover = Hex(0x303030);
            TrackSelected = Hex(0x3F3F3F);
            AccentDisabled = Hex(0x434343);
            TextOnAccentDisabled = Hex(0xA0A0A0);
            Success = Hex(0x6CCB5F);
            Danger = Hex(0xFF99A4);
            Caution = Hex(0xFCE100);
            NeutralSubtle = Hex(0x3A3A3A);
            JsonKey = Hex(0x9CDCFE);
            JsonString = Hex(0xCE9178);
            JsonNumber = Hex(0xB5CEA8);
            JsonLiteral = Hex(0x569CD6);
            JsonPunctuation = Hex(0xA0A0A0);
        }
        else
        {
            Window = Hex(0xF3F3F3);
            Surface = Hex(0xFFFFFF);
            SurfaceAlt = Hex(0xF8F8F8);
            Border = Hex(0xE5E5E5);
            Text = Hex(0x1B1B1B);
            SecondaryText = Hex(0x5F5F5F);
            TertiaryText = Hex(0x8A8A8A);
            DisabledText = Hex(0xA0A0A0);
            ControlFill = Hex(0xFBFBFB);
            ControlHover = Hex(0xF5F5F5);
            ControlPressed = Hex(0xEFEFEF);
            ControlBorder = Hex(0xDEDEDE);
            ControlStrongBorder = Hex(0x8A8A8A);
            InputFocused = Hex(0xFFFFFF);
            SubtleHover = Hex(0xF4F4F4);
            SubtlePressed = Hex(0xEEEEEE);
            SubtleSelected = Hex(0xEEEEEE);
            TrackFill = Hex(0xF3F3F3);
            TrackHover = Hex(0xEAEAEA);
            TrackSelected = Hex(0xFFFFFF);
            AccentDisabled = Hex(0xC5C5C5);
            TextOnAccentDisabled = Hex(0xFFFFFF);
            Success = Hex(0x0F7B0F);
            Danger = Hex(0xC42B1C);
            Caution = Hex(0x9D5D00);
            NeutralSubtle = Hex(0xF0F0F0);
            JsonKey = Hex(0x0451A5);
            JsonString = Hex(0xA31515);
            JsonNumber = Hex(0x098658);
            JsonLiteral = Hex(0x0000FF);
            JsonPunctuation = Hex(0x707070);
        }

        // AccentPalette: Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3. Windows 11 fills
        // accent buttons with Light2 in dark mode and Dark1 in light mode.
        var palette = ReadAccentPalette();
        BaseAccent = palette?[3] ?? Hex(0x0078D4);
        Accent = palette?[IsDark ? 1 : 4] ?? (IsDark ? Hex(0x60CDFF) : Hex(0x005FB8));
        AccentText = palette?[IsDark ? 0 : 5] ?? (IsDark ? Hex(0x99EBFF) : Hex(0x003E92));
        AccentHover = Blend(Accent, Surface, 0.1);
        AccentPressed = Blend(Accent, Surface, 0.2);
        AccentSubtle = Blend(Surface, Accent, IsDark ? 0.2 : 0.1);
        TextOnAccent = Accent.GetBrightness() > 0.55f ? Color.Black : Color.White;
    }

    public static Font Icon(float size)
    {
        if (!IconFonts.TryGetValue(size, out var font))
            IconFonts[size] = font = new Font(IconFamily, size);
        return font;
    }

    /// <summary>A soft background for a status color (chips, pills, icon circles).</summary>
    public static Color Tint(Color color, Color background) => Blend(background, color, IsDark ? 0.2 : 0.12);

    public static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        if (radius <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, RectangleF bounds, float radius, Color fill, Color border = default)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(bounds, radius);
        if (!fill.IsEmpty)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        if (!border.IsEmpty)
        {
            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = mode;
    }

    public static void DrawGlyph(Graphics g, string glyph, float size, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(g, glyph, Icon(size), bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

    private const TextFormatFlags ChipText = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    public static int MeasureChip(Control owner, string text, string? glyph = null, Font? font = null)
    {
        int width = TextRenderer.MeasureText(text, font ?? Caption, Size.Empty, ChipText).Width + owner.LogicalToDeviceUnits(16);
        if (glyph is not null)
            width += owner.LogicalToDeviceUnits(16);
        return width;
    }

    /// <summary>Draws a small rounded label and returns its width.</summary>
    public static int DrawChip(Graphics g, Control owner, int x, int y, string text, Color fore, Color back,
        string? glyph = null, Font? font = null, int height = 24)
    {
        font ??= Caption;
        int width = MeasureChip(owner, text, glyph, font);
        height = owner.LogicalToDeviceUnits(height);
        FillRounded(g, new RectangleF(x, y, width, height), owner.LogicalToDeviceUnits(4), back);
        int textX = x + owner.LogicalToDeviceUnits(8);
        if (glyph is not null)
        {
            DrawGlyph(g, glyph, 8F, new Rectangle(textX - owner.LogicalToDeviceUnits(1), y, owner.LogicalToDeviceUnits(14), height), fore);
            textX += owner.LogicalToDeviceUnits(16);
        }
        TextRenderer.DrawText(g, text, font, new Rectangle(textX, y, width, height), fore,
            ChipText | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        return width;
    }

    /// <summary>Dark title bar in dark mode, and a caption color that matches the window.</summary>
    public static void ApplyTitleBar(Form form)
    {
        try
        {
            int dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            int caption = ColorTranslator.ToWin32(Window);
            DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref caption, sizeof(int));
            int text = ColorTranslator.ToWin32(Text);
            DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref text, sizeof(int));
        }
        catch
        {
            // Older Windows without these attributes: keep the default title bar.
        }
    }

    /// <summary>Gives a control's own scroll bars the dark style in dark mode.</summary>
    public static void ApplyScrollBars(Control control)
    {
        if (!IsDark)
            return;
        try
        {
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
        catch
        {
            // Not fatal: the scroll bars stay light.
        }
    }

    /// <summary>The window icon: an accent-colored rounded square with a glyph.</summary>
    public static Icon CreateAppIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            FillRounded(g, new RectangleF(0.5f, 0.5f, 31, 31), 7, BaseAccent);
            using var font = new Font(IconFamily, 15F, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Glyphs.App, font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private static Color[]? ReadAccentPalette()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] bytes && bytes.Length >= 28)
                return Enumerable.Range(0, 7).Select(i => Color.FromArgb(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2])).ToArray();
        }
        catch
        {
            // Fall back to the Windows 11 default blue.
        }
        return null;
    }

    private static HashSet<string> LoadInstalledFonts()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Pick(params string[] families) =>
        families.FirstOrDefault(InstalledFonts.Contains) ?? families[^1];

    private static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}

/// <summary>Segoe Fluent Icons code points.</summary>
internal static class Glyphs
{
    public const string App = "";
    public const string Link = "";
    public const string Search = "";
    public const string Play = "";
    public const string Cancel = "";
    public const string Copy = "";
    public const string Check = "";
    public const string Error = "";
    public const string Warning = "";
    public const string Info = "";
    public const string Refresh = "";
    public const string Globe = "";
    public const string Plug = "";
    public const string Wrap = "";
    public const string Code = "";
    public const string Clock = "";
    public const string Form = "";
}
