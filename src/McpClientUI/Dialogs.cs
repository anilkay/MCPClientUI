namespace McpClientUI;

/// <summary>A Windows 11 style dialog: content on the surface color, OK / Cancel in a footer.</summary>
internal class FluentDialog : Form
{
    private readonly Panel body = new() { Dock = DockStyle.Fill };
    private readonly InlineStatus error = new() { Dock = DockStyle.Top, Visible = false };

    public FluentDialog(string title, string okText, int width)
    {
        Text = title;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;

        var ok = new FluentButton(okText, ButtonKind.Accent) { AutoSize = false, Size = new Size(S(120), S(32)) };
        var cancel = new FluentButton("Cancel") { AutoSize = false, Size = new Size(S(120), S(32)), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (TryAccept(out var problem))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                error.Set(Glyphs.Error, problem ?? "", Theme.Danger);
                error.Visible = true;
            }
        };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = S(72), BackColor = Theme.Window };
        footer.Controls.AddRange([ok, cancel]);
        footer.Layout += (_, _) =>
        {
            cancel.Location = new Point(footer.Width - S(24) - cancel.Width, (footer.Height - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - S(8) - ok.Width, cancel.Top);
        };
        body.Padding = new Padding(S(24), S(20), S(24), S(8));
        body.Controls.Add(error);
        Controls.Add(body);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(S(width), S(200));
    }

    protected int S(int value) => LogicalToDeviceUnits(value);

    /// <summary>Adds a control below the previous ones.</summary>
    protected void AddRow(Control control, int spaceAfter = 0)
    {
        control.Dock = DockStyle.Top;
        // Docked Top controls stack in reverse z-order: keep the error line last.
        body.Controls.Add(control);
        body.Controls.SetChildIndex(control, 1);
        if (spaceAfter > 0)
        {
            var gap = new Panel { Dock = DockStyle.Top, Height = S(spaceAfter) };
            body.Controls.Add(gap);
            body.Controls.SetChildIndex(gap, 1);
        }
    }

    protected Label CreateLabel(string text, bool strong = false, bool secondary = false)
    {
        var font = strong ? Theme.BodyStrong : secondary ? Theme.Caption : Theme.Body;
        int width = ClientSize.Width - S(48);
        int height = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + S(4);
        return new Label
        {
            Text = text,
            Font = font,
            ForeColor = secondary ? Theme.SecondaryText : Theme.Text,
            AutoSize = false,
            Height = height,
        };
    }

    /// <summary>Sizes the dialog to its rows; call at the end of the constructor.</summary>
    protected void FitHeight()
    {
        int height = body.Padding.Vertical + S(4);
        foreach (Control c in body.Controls)
            height += c.Height;
        ClientSize = new Size(ClientSize.Width, height + S(72));
    }

    protected virtual bool TryAccept(out string? problem)
    {
        problem = null;
        return true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }
}

/// <summary>Asks for a short name (e.g. for saving argument values).</summary>
internal sealed class NameDialog : FluentDialog
{
    private readonly InputBox name = new();

    public NameDialog(string title, string prompt, string initial) : base(title, "Save", 420)
    {
        name.Value = initial;
        AddRow(CreateLabel(prompt), 8);
        AddRow(name);
        FitHeight();
        Shown += (_, _) => { name.TextBox.Focus(); name.TextBox.SelectAll(); };
    }

    public string Value => name.Value.Trim();

    protected override bool TryAccept(out string? problem)
    {
        problem = Value.Length == 0 ? "Enter a name." : null;
        return problem is null;
    }
}

/// <summary>Bearer token and other HTTP headers sent with every request to one server.</summary>
internal sealed class HeadersDialog : FluentDialog
{
    private const string Bearer = "Bearer ";
    private readonly InputBox token = new(glyph: Glyphs.Key);
    private readonly InputBox others = new(multiline: true, mono: true);

    public HeadersDialog(string url, IReadOnlyDictionary<string, string> headers) : base("Request headers", "Save", 560)
    {
        token.TextBox.UseSystemPasswordChar = true;
        token.Placeholder = "Token (sent as Authorization: Bearer …)";
        others.Placeholder = "X-Api-Key: 123\r\nX-Team: qa";
        others.Height = S(120);

        var lines = new List<string>();
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && value.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
                token.Value = value[Bearer.Length..];
            else
                lines.Add($"{name}: {value}");
        }
        others.Value = string.Join("\r\n", lines);

        var show = new CheckBox { Text = "Show token", AutoSize = true, ForeColor = Theme.SecondaryText, Font = Theme.Caption, Height = S(28) };
        show.CheckedChanged += (_, _) => token.TextBox.UseSystemPasswordChar = !show.Checked;

        AddRow(CreateLabel(url, secondary: true), 12);
        AddRow(CreateLabel("Bearer token", strong: true), 6);
        AddRow(token, 2);
        AddRow(show, 12);
        AddRow(CreateLabel("Other headers", strong: true), 6);
        AddRow(others, 8);
        AddRow(CreateLabel("One per line, as Name: value. Headers are saved encrypted for your Windows account and sent with every request to this URL.", secondary: true));
        FitHeight();
    }

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    protected override bool TryAccept(out string? problem)
    {
        Headers.Clear();
        int lineNumber = 0;
        foreach (var line in others.Value.Split('\n'))
        {
            lineNumber++;
            string text = line.Trim();
            if (text.Length == 0) continue;
            int colon = text.IndexOf(':');
            string name = colon > 0 ? text[..colon].Trim() : "";
            if (name.Length == 0 || name.Any(c => c <= ' ' || c >= 127 || "()<>@,;:\\\"/[]?={}".Contains(c)))
            {
                problem = $"Line {lineNumber} should look like Name: value.";
                return false;
            }
            Headers[name] = text[(colon + 1)..].Trim();
        }

        string tokenText = token.Value.Trim();
        if (tokenText.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
            tokenText = tokenText[Bearer.Length..].Trim();
        if (tokenText.Length > 0)
        {
            if (Headers.ContainsKey("Authorization"))
            {
                problem = "Use either the Bearer token or an Authorization line, not both.";
                return false;
            }
            Headers["Authorization"] = Bearer + tokenText;
        }
        problem = null;
        return true;
    }
}

/// <summary>Environment variables for a stdio server's process (API keys, settings).</summary>
internal sealed class EnvironmentDialog : FluentDialog
{
    private readonly InputBox variables = new(multiline: true, mono: true);

    public EnvironmentDialog(string command, IReadOnlyDictionary<string, string> current) : base("Environment variables", "Save", 560)
    {
        variables.Placeholder = "API_KEY=123\r\nLOG_LEVEL=debug";
        variables.Height = S(150);
        variables.Value = string.Join("\r\n", current.Select(kv => $"{kv.Key}={kv.Value}"));

        AddRow(CreateLabel(command, secondary: true), 12);
        AddRow(CreateLabel("Variables", strong: true), 6);
        AddRow(variables, 8);
        AddRow(CreateLabel("One per line, as NAME=value. They are added to the server process's environment, saved encrypted for your Windows account.", secondary: true));
        FitHeight();
    }

    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    protected override bool TryAccept(out string? problem)
    {
        Variables.Clear();
        int lineNumber = 0;
        foreach (var line in variables.Value.Split('\n'))
        {
            lineNumber++;
            string text = line.Trim();
            if (text.Length == 0) continue;
            int equals = text.IndexOf('=');
            string name = equals > 0 ? text[..equals].Trim() : "";
            if (name.Length == 0 || name.Any(char.IsWhiteSpace))
            {
                problem = $"Line {lineNumber} should look like NAME=value.";
                return false;
            }
            Variables[name] = text[(equals + 1)..];
        }
        problem = null;
        return true;
    }
}
