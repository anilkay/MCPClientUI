using System.ComponentModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpClientUI;

/// <summary>Read-only text view with JSON syntax coloring.</summary>
internal sealed class CodeView : RichTextBox
{
    public CodeView()
    {
        BorderStyle = BorderStyle.None;
        ReadOnly = true;
        DetectUrls = false;
        HideSelection = false;
        ScrollBars = RichTextBoxScrollBars.Both;
        Font = Theme.Mono;
        BackColor = Theme.SurfaceAlt;
        ForeColor = Theme.Text;
    }

    /// <summary>The text as shown, without formatting (for copying).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Content { get; private set; } = "";

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyScrollBars(this);
    }

    public void ShowPlaceholder(string text)
    {
        Content = "";
        Rtf = RtfBuilder.Plain(text, Font, Theme.TertiaryText, ui: true);
    }

    public void ShowPlain(string text, Color? color = null)
    {
        Content = text;
        Rtf = RtfBuilder.Plain(text, Font, color ?? Theme.Text);
    }

    public void ShowJson(string json)
    {
        Content = json;
        Rtf = RtfBuilder.Json(json, Font);
    }

    /// <summary>Shows JSON with colors when <paramref name="text"/> is a JSON object or array, plain text otherwise.</summary>
    public void ShowText(string text)
    {
        if (JsonText.TryFormat(text, out var pretty))
            ShowJson(pretty);
        else
            ShowPlain(text);
    }
}


/// <summary>Builds small RTF documents (faster than coloring a RichTextBox selection by selection).</summary>
internal static class RtfBuilder
{
    private const int Key = 1, Str = 2, Number = 3, Literal = 4, Punctuation = 5;

    public static string Plain(string text, Font font, Color color, bool ui = false)
    {
        var writer = new Writer(ui ? Theme.Caption : font, [color]);
        writer.Write(text, 0);
        return writer.Finish();
    }

    public static string Json(string json, Font font)
    {
        var writer = new Writer(font, [Theme.Text, Theme.JsonKey, Theme.JsonString, Theme.JsonNumber, Theme.JsonLiteral, Theme.JsonPunctuation]);
        int i = 0;
        while (i < json.Length)
        {
            char c = json[i];
            int start = i;
            if (c == '"')
            {
                i++;
                while (i < json.Length && json[i] != '"')
                    i += json[i] == '\\' ? 2 : 1;
                i = Math.Min(i + 1, json.Length);
                int next = i;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                writer.Write(json[start..i], next < json.Length && json[next] == ':' ? Key : Str);
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                while (i < json.Length && (char.IsAsciiDigit(json[i]) || json[i] is '-' or '+' or '.' or 'e' or 'E')) i++;
                writer.Write(json[start..i], Number);
            }
            else if (char.IsAsciiLetter(c))
            {
                while (i < json.Length && char.IsAsciiLetter(json[i])) i++;
                writer.Write(json[start..i], Literal);
            }
            else if (c is '{' or '}' or '[' or ']' or ',' or ':')
            {
                i++;
                writer.Write(json[start..i], Punctuation);
            }
            else
            {
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i == start) i++;
                writer.Write(json[start..i], -1);
            }
        }
        return writer.Finish();
    }

    private sealed class Writer
    {
        private readonly StringBuilder rtf = new();
        private int color = -1;

        public Writer(Font font, Color[] colors)
        {
            // The family and pitch hints matter: without them RichEdit may substitute Courier New.
            string family = font.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase) || font.Name == "Consolas"
                ? @"\fmodern\fprq1" : @"\fswiss\fprq2";
            rtf.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0").Append(family).Append(@"\fcharset0 ")
               .Append(font.Name).Append(";}}{\\colortbl ;");
            foreach (var c in colors)
                rtf.Append("\\red").Append(c.R).Append("\\green").Append(c.G).Append("\\blue").Append(c.B).Append(';');
            rtf.Append("}\\uc1\\pard\\sl0\\slmult1\\f0\\fs").Append((int)Math.Round(font.SizeInPoints * 2)).Append(' ');
        }

        /// <summary>Appends text in the given color; -1 keeps the current color.</summary>
        public void Write(string text, int colorIndex)
        {
            if (colorIndex >= 0 && colorIndex != color)
            {
                rtf.Append("\\cf").Append(colorIndex + 1).Append(' ');
                color = colorIndex;
            }
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': rtf.Append(@"\\"); break;
                    case '{': rtf.Append(@"\{"); break;
                    case '}': rtf.Append(@"\}"); break;
                    case '\n': rtf.Append("\\par\n"); break;
                    case '\t': rtf.Append("\\tab "); break;
                    case '\r': break;
                    default:
                        if (c < 0x20) break;
                        if (c > 0x7E) rtf.Append("\\u").Append((short)c).Append('?');
                        else rtf.Append(c);
                        break;
                }
            }
        }

        public string Finish() => rtf.Append('}').ToString();
    }
}
