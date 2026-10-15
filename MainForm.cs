using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpClientUI;

/// <summary>
/// MCP client: connect to a Streamable HTTP server, browse its tools, fill in arguments
/// and call them.
/// </summary>
internal sealed class MainForm : Form
{
    private sealed record ResultState(ResultStatus Status, string Meta, string Text);

    private readonly AppSettings settings = AppSettings.Load();
    private readonly ToolTip toolTip = new();

    // Header
    private readonly InputBox urlBox = new(glyph: Glyphs.Link);
    private readonly FluentButton connectButton = new("Connect", ButtonKind.Accent);
    private readonly StatusPill statusPill = new();

    // Sidebar
    private readonly InputBox searchBox = new(glyph: Glyphs.Search) { Dock = DockStyle.Top };
    private readonly ServerItem serverItem = new() { Dock = DockStyle.Top, Visible = false };
    private readonly FluentButton refreshButton = new("", ButtonKind.Subtle, Glyphs.Refresh) { Enabled = false };
    private readonly SectionHeader toolsHeader;
    private readonly ToolListBox toolList = new() { Dock = DockStyle.Fill };

    // Right side
    private readonly EmptyState emptyState = new() { Dock = DockStyle.Fill };
    private readonly Panel toolView = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly ToolHeader toolHeader = new() { Dock = DockStyle.Top };
    private readonly PivotHeader pivot = new("Arguments", "Raw JSON", "Schema") { Dock = DockStyle.Top };
    private readonly ArgumentEditor argumentEditor = new() { Dock = DockStyle.Fill };
    private readonly Panel rawPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly InputBox rawBox = new(multiline: true, mono: true) { Dock = DockStyle.Fill };
    private readonly InlineStatus rawStatus = new();
    private readonly Panel schemaPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly CodeView schemaView = new();
    private readonly ResultHeader resultHeader = new() { Dock = DockStyle.Top };
    private readonly ProgressLine progress = new() { Dock = DockStyle.Top };
    private readonly CodeView resultView = new();
    private readonly SplitContainer mainSplit;
    private readonly SplitContainer workSplit;

    private readonly System.Windows.Forms.Timer elapsedTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer copiedTimer = new() { Interval = 1500 };
    private readonly Stopwatch callWatch = new();
    private readonly Dictionary<string, ResultState> results = [];

    private McpClient? client;
    private ConnectionState state;
    private string? connectError;
    private List<McpClientTool> tools = [];
    private McpClientTool? currentTool;
    private CancellationTokenSource? callCts;
    private string? runningTool;
    private bool suppressSelection, workSplitReady;

    public MainForm()
    {
        Text = "MCP Client";
        Font = Theme.Body;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Icon = Theme.CreateAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(S(980), S(660));
        Size = new Size(S(1320), S(860));

        // Header: app name, server URL, connect button, connection status.
        urlBox.Value = settings.Url;
        urlBox.Placeholder = "http://localhost:5199/mcp";
        urlBox.TextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        urlBox.TextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        urlBox.TextBox.AutoCompleteCustomSource.AddRange([.. settings.RecentUrls]);
        var header = new HeaderBar(urlBox, connectButton, statusPill) { Dock = DockStyle.Top };

        // Sidebar: search, server, tool list. Fill first, then Top panels bottom-up (WinForms docks in reverse z-order).
        searchBox.Placeholder = "Search tools";
        toolsHeader = new SectionHeader("TOOLS", refreshButton) { Dock = DockStyle.Top };
        toolList.EmptyText = "Connect to a server to see its tools.";
        var sidebar = new Card { Dock = DockStyle.Fill };
        sidebar.Padding = new Padding(S(12), S(12), S(12), S(8));
        sidebar.Controls.Add(toolList);
        sidebar.Controls.Add(toolsHeader);
        sidebar.Controls.Add(serverItem);
        sidebar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = S(6) });
        sidebar.Controls.Add(searchBox);

        // Arguments card: Arguments / Raw JSON / Schema pages under a pivot.
        var formatButton = new FluentButton("Format", ButtonKind.Standard, Glyphs.Code);
        var fromFormButton = new FluentButton("Copy from form", ButtonKind.Standard, Glyphs.Form);
        rawStatus.Size = new Size(S(420), S(32));
        rawStatus.Margin = new Padding(S(12), 0, 0, 0);
        formatButton.Margin = new Padding(0, 0, S(8), 0);
        fromFormButton.Margin = Padding.Empty;
        var rawToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(44), WrapContents = false, Padding = Padding.Empty };
        rawToolbar.Controls.AddRange([formatButton, fromFormButton, rawStatus]);
        rawPage.Padding = new Padding(S(16), S(12), S(16), S(16));
        rawPage.Controls.Add(rawBox);
        rawPage.Controls.Add(rawToolbar);
        schemaPage.Padding = new Padding(S(16), S(14), S(16), S(16));
        schemaPage.Controls.Add(new CodePanel(schemaView) { Dock = DockStyle.Fill });
        schemaView.WordWrap = false;

        var argsCard = new Card { Dock = DockStyle.Fill };
        argsCard.Controls.Add(argumentEditor);
        argsCard.Controls.Add(rawPage);
        argsCard.Controls.Add(schemaPage);
        argsCard.Controls.Add(pivot);

        // Result card: header with status, progress line, result text.
        var resultBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(16), S(2), S(16), S(16)) };
        resultBody.Controls.Add(new CodePanel(resultView) { Dock = DockStyle.Fill });
        var resultCard = new Card { Dock = DockStyle.Fill };
        resultCard.Controls.Add(resultBody);
        resultCard.Controls.Add(progress);
        resultCard.Controls.Add(resultHeader);

        workSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = S(12),
            BackColor = Theme.Window,
            TabStop = false,
        };
        workSplit.Panel1.Controls.Add(argsCard);
        workSplit.Panel2.Controls.Add(resultCard);

        toolView.BackColor = Theme.Window;
        toolView.Controls.Add(workSplit);
        toolView.Controls.Add(new Panel { Dock = DockStyle.Top, Height = S(12), BackColor = Theme.Window });
        toolView.Controls.Add(toolHeader);

        mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = S(12),
            BackColor = Theme.Window,
            FixedPanel = FixedPanel.Panel1,
            TabStop = false,
        };
        mainSplit.Panel1.Controls.Add(sidebar);
        mainSplit.Panel2.Controls.Add(toolView);
        mainSplit.Panel2.Controls.Add(emptyState);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(16), 0, S(16), S(16)), BackColor = Theme.Window };
        body.Controls.Add(mainSplit);
        Controls.Add(body);
        Controls.Add(header);

        toolTip.SetToolTip(toolHeader.RunButton, "Run the tool (Ctrl+Enter)");
        toolTip.SetToolTip(toolHeader.CancelButton, "Cancel the call (Esc)");
        toolTip.SetToolTip(refreshButton, "Reload the tool list (F5)");
        toolTip.SetToolTip(searchBox.TextBox, "Filter tools by name or description (Ctrl+F)");
        toolTip.SetToolTip(serverItem, "Server overview");

        connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        urlBox.TextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (client is null) await ToggleConnectionAsync();
        };
        urlBox.TextBox.TextChanged += (_, _) => urlBox.HasError = false;
        statusPill.Click += (_, _) => ShowOverview();
        serverItem.Click += (_, _) => ShowOverview();
        refreshButton.Click += async (_, _) => await RefreshToolsAsync();
        searchBox.TextBox.TextChanged += (_, _) => ApplyFilter();
        searchBox.TextBox.KeyDown += SearchKeyDown;
        toolList.SelectedIndexChanged += (_, _) => OnListSelection();
        pivot.SelectedIndexChanged += (_, _) => ShowPage();
        toolHeader.RunButton.Click += async (_, _) => await RunAsync();
        toolHeader.CancelButton.Click += (_, _) => callCts?.Cancel();
        argumentEditor.SubmitRequested += async (_, _) => await RunAsync();
        formatButton.Click += (_, _) => FormatRaw();
        fromFormButton.Click += (_, _) => rawBox.Value = argumentEditor.GetFilledArguments().ToJsonString(JsonText.Pretty).ReplaceLineEndings("\r\n");
        rawBox.TextBox.TextChanged += (_, _) => rawBox.HasError = !TryParseRaw(out _);
        resultHeader.WrapButton.Click += (_, _) =>
        {
            resultView.WordWrap = !resultView.WordWrap;
            resultHeader.WrapButton.Checked = resultView.WordWrap;
        };
        resultHeader.CopyButton.Click += (_, _) => CopyResult();
        elapsedTimer.Tick += (_, _) =>
        {
            if (runningTool is not null && currentTool?.Name == runningTool)
                resultHeader.SetStatus(ResultStatus.Running, Elapsed());
        };
        copiedTimer.Tick += (_, _) =>
        {
            copiedTimer.Stop();
            resultHeader.CopyButton.Text = "Copy";
            resultHeader.CopyButton.Glyph = Glyphs.Copy;
        };

        SetState(ConnectionState.Disconnected);
        ShowPage();
        RestoreWindow();
    }

    private int S(int value) => LogicalToDeviceUnits(value);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            mainSplit.Panel1MinSize = S(240);
            mainSplit.Panel2MinSize = S(480);
            mainSplit.SplitterDistance = settings.SidebarWidth > 0 ? Math.Clamp(settings.SidebarWidth, S(240), S(560)) : S(310);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The window is too small for the saved layout; keep the default.
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        urlBox.TextBox.Focus();
        urlBox.TextBox.SelectionStart = urlBox.Value.Length;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.WindowBounds = [bounds.X, bounds.Y, bounds.Width, bounds.Height];
        settings.Maximized = WindowState == FormWindowState.Maximized;
        settings.SidebarWidth = mainSplit.SplitterDistance;
        settings.Save();
        callCts?.Cancel();
        if (client is not null) _ = SafeDisposeAsync(client);
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Enter:
                _ = RunAsync();
                return true;
            case Keys.Control | Keys.F:
                searchBox.TextBox.Focus();
                searchBox.TextBox.SelectAll();
                return true;
            case Keys.F5:
                _ = RefreshToolsAsync();
                return true;
            case Keys.Escape when callCts is not null:
                callCts.Cancel();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RestoreWindow()
    {
        if (settings.WindowBounds is [var x, var y, var width, var height] && width >= MinimumSize.Width && height >= MinimumSize.Height)
        {
            var bounds = new Rectangle(x, y, width, height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
        }
        if (settings.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    // ---------------------------------------------------------------- connection

    private async Task ToggleConnectionAsync()
    {
        if (client is not null)
        {
            await DisconnectAsync();
            return;
        }
        if (state == ConnectionState.Connecting) return;

        string url = urlBox.Value.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            urlBox.HasError = true;
            connectError = "That isn't a server address. Enter an http:// or https:// URL, for example http://localhost:5199/mcp.";
            SetState(ConnectionState.Failed);
            urlBox.TextBox.Focus();
            return;
        }

        connectError = null;
        SetState(ConnectionState.Connecting);
        McpClient? newClient = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "MCP Client",
            });
            newClient = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "mcp-client-ui", Version = "1.0" },
            }, cancellationToken: timeout.Token);
            var list = await newClient.ListToolsAsync(cancellationToken: timeout.Token);

            client = newClient;
            tools = [.. list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
            currentTool = null;
            results.Clear();
            UpdateServerItem();

            settings.RememberUrl(url);
            settings.Save();
            if (!urlBox.TextBox.AutoCompleteCustomSource.Contains(url))
                urlBox.TextBox.AutoCompleteCustomSource.Add(url);

            SetState(ConnectionState.Connected);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            if (newClient is not null) await SafeDisposeAsync(newClient);
            connectError = ex is OperationCanceledException ? "The server didn't answer within 20 seconds." : ex.Message;
            SetState(ConnectionState.Failed);
        }
    }

    private async Task DisconnectAsync()
    {
        callCts?.Cancel();
        var old = client;
        client = null;
        tools = [];
        currentTool = null;
        results.Clear();
        ApplyFilter();
        SetState(ConnectionState.Disconnected);
        if (old is not null) await SafeDisposeAsync(old);
    }

    private static async Task SafeDisposeAsync(McpClient mcpClient)
    {
        try { await mcpClient.DisposeAsync(); }
        catch (Exception) { /* The server may already be gone. */ }
    }

    private async Task RefreshToolsAsync()
    {
        if (client is null || !refreshButton.Enabled) return;
        refreshButton.Enabled = false;
        try
        {
            var list = await client.ListToolsAsync();
            tools = [.. list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
            var previous = currentTool;
            currentTool = null;
            UpdateServerItem();
            ApplyFilter();
            if (previous is not null && tools.FirstOrDefault(t => t.Name == previous.Name) is { } reloaded)
            {
                SelectTool(reloaded);
                ApplyFilter();
            }
            else
            {
                UpdateRightPane();
            }
        }
        catch (Exception ex)
        {
            statusPill.Set(ConnectionState.Failed, "Couldn't reload tools");
            toolTip.SetToolTip(statusPill, ex.Message);
        }
        finally
        {
            refreshButton.Enabled = client is not null;
        }
    }

    private void SetState(ConnectionState newState)
    {
        state = newState;
        bool connected = newState == ConnectionState.Connected;
        string serverName = client is null ? "" : ServerName(client);
        statusPill.Set(newState, newState switch
        {
            ConnectionState.Connecting => "Connecting…",
            ConnectionState.Connected => $"Connected to {serverName}",
            ConnectionState.Failed => "Connection failed",
            _ => "Not connected",
        });
        toolTip.SetToolTip(statusPill, connected ? "Show the server overview" : newState == ConnectionState.Failed ? connectError : null);
        statusPill.Cursor = connected ? Cursors.Hand : Cursors.Default;

        connectButton.Text = connected ? "Disconnect" : newState == ConnectionState.Connecting ? "Connecting…" : "Connect";
        connectButton.Kind = connected ? ButtonKind.Standard : ButtonKind.Accent;
        connectButton.Enabled = newState != ConnectionState.Connecting;
        urlBox.TextBox.ReadOnly = newState is ConnectionState.Connected or ConnectionState.Connecting;
        refreshButton.Enabled = connected;
        serverItem.Visible = connected;
        connectButton.Parent?.PerformLayout();
        UpdateRunButtons();
        UpdateRightPane();
    }

    private static string ServerName(McpClient mcpClient) =>
        string.IsNullOrWhiteSpace(mcpClient.ServerInfo.Title) ? mcpClient.ServerInfo.Name : mcpClient.ServerInfo.Title;

    private void UpdateServerItem()
    {
        if (client is null) return;
        serverItem.Set(ServerName(client), $"Version {client.ServerInfo.Version} · {Plural(tools.Count, "tool")}");
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    // ---------------------------------------------------------------- tool list

    private void ApplyFilter()
    {
        string query = searchBox.Value.Trim();
        var visible = tools.Where(t => query.Length == 0 ||
                                       t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                       (t.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        suppressSelection = true;
        toolList.BeginUpdate();
        toolList.Items.Clear();
        foreach (var tool in visible)
            toolList.Items.Add(new ToolItem(tool));
        int index = currentTool is null ? -1 : visible.IndexOf(currentTool);
        if (index >= 0)
            toolList.SelectedIndex = index;
        toolList.EndUpdate();
        suppressSelection = false;

        toolsHeader.Count = tools.Count == 0 ? "" : query.Length == 0 ? $"{tools.Count:N0}" : $"{visible.Count:N0} of {tools.Count:N0}";
        toolList.EmptyText = client is null ? "Connect to a server to see its tools."
            : tools.Count == 0 ? "This server has no tools."
            : "No tools match your search.";
    }

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down when toolList.Items.Count > 0:
                toolList.SelectedIndex = Math.Min(toolList.Items.Count - 1, toolList.SelectedIndex + 1);
                e.SuppressKeyPress = true;
                break;
            case Keys.Up when toolList.Items.Count > 0:
                toolList.SelectedIndex = Math.Max(0, toolList.SelectedIndex - 1);
                e.SuppressKeyPress = true;
                break;
            case Keys.Enter when toolList.Items.Count > 0:
                if (toolList.SelectedIndex < 0) toolList.SelectedIndex = 0;
                if (argumentEditor.HasFields && pivot.SelectedIndex == 0) argumentEditor.FocusFirst();
                else toolHeader.RunButton.Focus();
                e.SuppressKeyPress = true;
                break;
            case Keys.Escape when searchBox.Value.Length > 0:
                searchBox.Value = "";
                e.SuppressKeyPress = true;
                break;
        }
    }

    private void OnListSelection()
    {
        if (suppressSelection) return;
        if (toolList.SelectedItem is ToolItem item && !ReferenceEquals(item.Tool, currentTool))
            SelectTool(item.Tool);
    }

    private void ShowOverview()
    {
        if (client is null) return;
        currentTool = null;
        suppressSelection = true;
        toolList.ClearSelected();
        suppressSelection = false;
        UpdateRunButtons();
        UpdateRightPane();
    }

    private void SelectTool(McpClientTool tool)
    {
        currentTool = tool;
        var schema = tool.ProtocolTool.InputSchema;
        toolHeader.SetTool(tool);
        argumentEditor.Load(schema);
        schemaView.ShowJson(JsonText.Format(schema));
        rawBox.Value = BuildRawTemplate(schema);
        ShowResult();
        UpdateRunButtons();
        UpdateRightPane();
        FitArgumentsCard();
    }

    private void UpdateRightPane()
    {
        serverItem.Selected = client is not null && currentTool is null;
        if (client is not null && currentTool is not null)
        {
            emptyState.Visible = false;
            toolView.Visible = true;
            EnsureWorkSplit();
            return;
        }

        toolView.Visible = false;
        emptyState.Visible = true;
        switch (state)
        {
            case ConnectionState.Connected when client is not null:
                var info = client.ServerInfo;
                string instructions = string.IsNullOrWhiteSpace(client.ServerInstructions)
                    ? "This server didn't send any instructions."
                    : client.ServerInstructions.Trim();
                var chips = new List<string> { $"Version {info.Version}" };
                if (!string.IsNullOrEmpty(client.NegotiatedProtocolVersion)) chips.Add($"Protocol {client.NegotiatedProtocolVersion}");
                chips.Add(Plural(tools.Count, "tool"));
                if (client.ServerCapabilities?.Resources is not null) chips.Add("Resources");
                if (client.ServerCapabilities?.Prompts is not null) chips.Add("Prompts");
                emptyState.Show(Glyphs.Globe, ServerName(client), instructions + "\n\nPick a tool on the left to try it.", Theme.Accent, [.. chips]);
                break;
            case ConnectionState.Connecting:
                emptyState.Show(Glyphs.Plug, "Connecting…", urlBox.Value.Trim(), Theme.Accent);
                break;
            case ConnectionState.Failed:
                emptyState.Show(Glyphs.Error, "Couldn't connect",
                    (connectError ?? "") + "\n\nCheck that the server is running and that the URL is right.", Theme.Danger);
                break;
            default:
                emptyState.Show(Glyphs.Plug, "Connect to an MCP server",
                    "Enter the server's Streamable HTTP address above and press Connect. Its tools appear on the left, and you can call any of them with your own arguments.",
                    Theme.Accent);
                break;
        }
    }

    private void EnsureWorkSplit()
    {
        if (workSplitReady) return;
        toolView.PerformLayout();
        if (workSplit.Height < S(360)) return;
        try
        {
            workSplit.Panel1MinSize = S(150);
            workSplit.Panel2MinSize = S(150);
            workSplitReady = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Try again once the window is bigger.
        }
    }

    /// <summary>Gives the arguments card the room its form needs (up to 60%), and the result the rest.</summary>
    private void FitArgumentsCard()
    {
        if (!workSplitReady) return;
        int needed = pivot.Height + argumentEditor.MeasureContentHeight(workSplit.Width - S(8)) + S(10);
        int minimum = S(190), maximum = Math.Max(minimum, (int)(workSplit.Height * 0.6));
        try
        {
            workSplit.SplitterDistance = Math.Clamp(needed, minimum, maximum);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Window too small to honor the minimum sizes; keep the current split.
        }
    }

    // ---------------------------------------------------------------- arguments

    private void ShowPage()
    {
        int page = pivot.SelectedIndex;
        argumentEditor.Visible = page == 0;
        rawPage.Visible = page == 1;
        schemaPage.Visible = page == 2;
        pivot.Hint = page switch
        {
            0 => "Run sends these values · Ctrl+Enter",
            1 => "Run sends this JSON · Ctrl+Enter",
            _ => "",
        };
    }

    /// <summary>Required arguments as empty values of their type, plus optional ones that have a default.</summary>
    private static string BuildRawTemplate(JsonElement schema)
    {
        var template = new JsonObject();
        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            var required = schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
                ? req.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToHashSet()
                : [];
            foreach (var p in props.EnumerateObject())
            {
                if (p.Value.TryGetProperty("default", out var def))
                {
                    template[p.Name] = JsonNode.Parse(def.GetRawText());
                    continue;
                }
                if (!required.Contains(p.Name)) continue;
                string type = p.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "string";
                template[p.Name] = type switch
                {
                    "integer" or "number" => 0,
                    "boolean" => false,
                    "array" => new JsonArray(),
                    "object" => new JsonObject(),
                    _ => "",
                };
            }
        }
        return template.ToJsonString(JsonText.Pretty).ReplaceLineEndings("\r\n");
    }

    private bool TryParseRaw(out JsonObject arguments)
    {
        arguments = [];
        string text = rawBox.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            rawStatus.Set(Glyphs.Info, "Empty: the tool is called without arguments", Theme.SecondaryText);
            return true;
        }
        try
        {
            if (JsonNode.Parse(text) is JsonObject parsed)
            {
                arguments = parsed;
                rawStatus.Set(Glyphs.Check, parsed.Count == 0 ? "Valid JSON, no arguments" : $"Valid JSON · {Plural(parsed.Count, "argument")}", Theme.Success);
                return true;
            }
            rawStatus.Set(Glyphs.Error, "Arguments must be a JSON object: { … }", Theme.Danger);
        }
        catch (JsonException ex)
        {
            rawStatus.Set(Glyphs.Error, $"Not valid JSON ({JsonText.Describe(ex)})", Theme.Danger);
        }
        return false;
    }

    private void FormatRaw()
    {
        if (!string.IsNullOrWhiteSpace(rawBox.Value) && TryParseRaw(out var parsed))
            rawBox.Value = parsed.ToJsonString(JsonText.Pretty).ReplaceLineEndings("\r\n");
    }

    // ---------------------------------------------------------------- calling

    private void UpdateRunButtons()
    {
        bool running = callCts is not null;
        toolHeader.RunButton.Enabled = client is not null && !running;
        toolHeader.CancelButton.Visible = running && currentTool is not null && currentTool.Name == runningTool;
    }

    private async Task RunAsync()
    {
        var tool = currentTool;
        var mcpClient = client;
        if (tool is null || mcpClient is null || callCts is not null) return;

        JsonObject arguments;
        if (pivot.SelectedIndex == 1)
        {
            if (!TryParseRaw(out arguments))
            {
                rawBox.HasError = true;
                rawBox.TextBox.Focus();
                return;
            }
        }
        else if (!argumentEditor.TryGetArguments(out arguments))
        {
            pivot.SelectedIndex = 0;
            return;
        }
        var callArguments = arguments.ToDictionary(
            kv => kv.Key,
            kv => (object?)(kv.Value is null ? null : JsonSerializer.SerializeToElement(kv.Value)));

        var cts = new CancellationTokenSource();
        callCts = cts;
        runningTool = tool.Name;
        results.Remove(tool.Name);
        callWatch.Restart();
        elapsedTimer.Start();
        ShowResult();
        UpdateRunButtons();

        ResultState outcome;
        try
        {
            var result = await mcpClient.CallToolAsync(tool.Name, callArguments, cancellationToken: cts.Token);
            string text = FormatResult(result);
            callWatch.Stop();
            outcome = new ResultState(result.IsError == true ? ResultStatus.ToolError : ResultStatus.Success, Meta(text), text);
        }
        catch (OperationCanceledException)
        {
            callWatch.Stop();
            outcome = new ResultState(ResultStatus.Cancelled, Meta(null), "The call was cancelled before the server answered.");
        }
        catch (Exception ex)
        {
            callWatch.Stop();
            outcome = new ResultState(ResultStatus.Failed, Meta(null), ex.Message);
        }
        finally
        {
            elapsedTimer.Stop();
            callCts = null;
            runningTool = null;
            cts.Dispose();
        }

        if (ReferenceEquals(client, mcpClient))
            results[tool.Name] = outcome;
        if (currentTool?.Name == tool.Name)
            ShowResult();
        UpdateRunButtons();
    }

    private string Elapsed()
    {
        double seconds = callWatch.Elapsed.TotalSeconds;
        return seconds < 10 ? $"{seconds:0.00} s" : $"{seconds:0.0} s";
    }

    private string Meta(string? text)
    {
        string meta = Elapsed();
        if (text is not null)
            meta += " · " + Plural(text.Count(c => c == '\n') + 1, "line");
        return meta;
    }

    private void ShowResult()
    {
        var tool = currentTool;
        bool running = tool is not null && runningTool == tool.Name;
        progress.Running = running;
        if (running)
        {
            resultHeader.SetStatus(ResultStatus.Running, Elapsed());
            resultView.ShowPlaceholder("Waiting for the server…");
        }
        else if (tool is not null && results.TryGetValue(tool.Name, out var result))
        {
            resultHeader.SetStatus(result.Status, result.Meta);
            switch (result.Status)
            {
                case ResultStatus.Failed:
                    resultView.ShowPlain(result.Text, Theme.Danger);
                    break;
                case ResultStatus.Cancelled:
                    resultView.ShowPlain(result.Text, Theme.SecondaryText);
                    break;
                default:
                    resultView.ShowText(result.Text);
                    break;
            }
        }
        else
        {
            resultHeader.SetStatus(ResultStatus.None, "");
            resultView.ShowPlaceholder("Run the tool to see its result here.");
        }
        resultHeader.CopyButton.Enabled = resultView.Content.Length > 0;
    }

    private static string FormatResult(CallToolResult result)
    {
        var parts = new List<string>();
        foreach (var block in result.Content)
        {
            parts.Add(block switch
            {
                TextContentBlock text => JsonText.TryFormat(text.Text, out var pretty) ? pretty : text.Text,
                ImageContentBlock image => $"[image: {image.MimeType}, {image.DecodedData.Length:N0} bytes]",
                _ => DescribeBlock(block),
            });
        }
        if (parts.Count == 0 && result.StructuredContent is { } structured)
            parts.Add(JsonText.Format(structured));
        return parts.Count == 0 ? "(The tool returned no content.)" : string.Join("\n\n", parts);
    }

    private static string DescribeBlock(ContentBlock block)
    {
        try
        {
            return JsonSerializer.Serialize(block, JsonText.Pretty);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return $"[{block.Type} content]";
        }
    }

    private void CopyResult()
    {
        if (resultView.Content.Length == 0) return;
        try
        {
            Clipboard.SetText(resultView.Content);
        }
        catch (ExternalException)
        {
            return;
        }
        resultHeader.CopyButton.Text = "Copied";
        resultHeader.CopyButton.Glyph = Glyphs.Check;
        copiedTimer.Stop();
        copiedTimer.Start();
    }
}
