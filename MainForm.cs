using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpClientUI;

/// <summary>
/// MCP client: connect to a Streamable HTTP server, browse its tools, resources and prompts,
/// fill in arguments and call them; keeps a call history and saved argument sets.
/// </summary>
internal sealed class MainForm : Form
{
    private sealed record ResultState(ResultStatus Status, string Meta, string Text);

    private const int ToolsTab = 0, ResourcesTab = 1, PromptsTab = 2, HistoryTab = 3;

    private readonly AppSettings settings = AppSettings.Load();
    private readonly HistoryStore history = HistoryStore.Load();
    private readonly ToolTip toolTip = new();

    // Header
    private readonly InputBox urlBox = new(glyph: Glyphs.Link);
    private readonly FluentButton headersButton = new("Headers", ButtonKind.Standard, Glyphs.Key);
    private readonly FluentButton connectButton = new("Connect", ButtonKind.Accent);
    private readonly StatusPill statusPill = new();

    // Sidebar
    private readonly InputBox searchBox = new(glyph: Glyphs.Search) { Dock = DockStyle.Top };
    private readonly ServerItem serverItem = new() { Dock = DockStyle.Top, Visible = false };
    private readonly PivotHeader sidebarPivot = new("Tools", "Resources", "Prompts", "History") { Dock = DockStyle.Top, Inset = 4 };
    private readonly FluentButton refreshButton = new("", ButtonKind.Subtle, Glyphs.Refresh) { Enabled = false };
    private readonly FluentButton clearHistoryButton = new("", ButtonKind.Subtle, Glyphs.Delete) { Visible = false };
    private readonly SectionHeader listHeader;
    private readonly ItemListBox list = new() { Dock = DockStyle.Fill };

    // Right side
    private readonly EmptyState emptyState = new() { Dock = DockStyle.Fill };
    private readonly Panel itemView = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly ToolHeader itemHeader = new() { Dock = DockStyle.Top };
    private readonly PivotHeader pivot = new("Arguments", "Raw JSON", "Schema") { Dock = DockStyle.Top };
    private readonly FluentButton presetsButton = new("Saved", ButtonKind.Subtle, Glyphs.Save);
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
    private string connectedUrl = "";
    private ConnectionState state;
    private string? connectError;
    private List<McpItem> tools = [], resources = [], prompts = [];
    private McpItem? currentItem;
    private CancellationTokenSource? callCts;
    private string? runningId;
    private bool suppressSelection, workSplitReady;

    public MainForm()
    {
        Text = "MCP Client";
        Font = Theme.Body;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Icon = Theme.AppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(S(1040), S(680));
        Size = new Size(S(1320), S(860));

        // Header: app name, server URL, headers, connect button, connection status.
        urlBox.Value = settings.Url;
        urlBox.Placeholder = "http://localhost:5199/mcp";
        urlBox.TextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        urlBox.TextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        urlBox.TextBox.AutoCompleteCustomSource.AddRange([.. settings.RecentUrls]);
        var header = new HeaderBar(urlBox, headersButton, connectButton, statusPill) { Dock = DockStyle.Top };

        // Sidebar: search, server, Tools / Resources / Prompts / History. Fill first, then Top panels
        // bottom-up (WinForms docks in reverse z-order).
        searchBox.Placeholder = "Search";
        listHeader = new SectionHeader("TOOLS", clearHistoryButton, refreshButton) { Dock = DockStyle.Top };
        var sidebar = new Card { Dock = DockStyle.Fill };
        sidebar.Padding = new Padding(S(12), S(12), S(12), S(8));
        sidebar.Controls.Add(list);
        sidebar.Controls.Add(listHeader);
        sidebar.Controls.Add(sidebarPivot);
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
        pivot.Action = presetsButton;
        pivot.AccessibleName = pivot.Text = "Argument views";
        sidebarPivot.AccessibleName = sidebarPivot.Text = "Sidebar sections";

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

        itemView.BackColor = Theme.Window;
        itemView.Controls.Add(workSplit);
        itemView.Controls.Add(new Panel { Dock = DockStyle.Top, Height = S(12), BackColor = Theme.Window });
        itemView.Controls.Add(itemHeader);

        mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = S(12),
            BackColor = Theme.Window,
            FixedPanel = FixedPanel.Panel1,
            TabStop = false,
        };
        mainSplit.Panel1.Controls.Add(sidebar);
        mainSplit.Panel2.Controls.Add(itemView);
        mainSplit.Panel2.Controls.Add(emptyState);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(16), 0, S(16), S(16)), BackColor = Theme.Window };
        body.Controls.Add(mainSplit);
        Controls.Add(body);
        Controls.Add(header);

        toolTip.SetToolTip(itemHeader.RunButton, "Run (Ctrl+Enter)");
        toolTip.SetToolTip(itemHeader.CancelButton, "Cancel the call (Esc)");
        toolTip.SetToolTip(refreshButton, "Reload from the server (F5)");
        toolTip.SetToolTip(clearHistoryButton, "Clear the history for this server");
        toolTip.SetToolTip(searchBox.TextBox, "Filter the list (Ctrl+F)");
        toolTip.SetToolTip(serverItem, "Server overview");
        toolTip.SetToolTip(headersButton, "Bearer token and other HTTP headers for this server");
        toolTip.SetToolTip(presetsButton, "Save these values, or load saved ones");

        connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        headersButton.Click += async (_, _) => await EditHeadersAsync();
        urlBox.TextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (client is null) await ToggleConnectionAsync();
        };
        urlBox.TextBox.TextChanged += (_, _) =>
        {
            urlBox.HasError = false;
            UpdateHeadersButton();
        };
        statusPill.Click += (_, _) => ShowOverview();
        serverItem.Click += (_, _) => ShowOverview();
        refreshButton.Click += async (_, _) => await RefreshItemsAsync();
        clearHistoryButton.Click += (_, _) => ClearHistory();
        searchBox.TextBox.TextChanged += (_, _) => RefreshList();
        searchBox.TextBox.KeyDown += SearchKeyDown;
        sidebarPivot.SelectedIndexChanged += (_, _) => RefreshList();
        list.SelectedIndexChanged += (_, _) => OnListSelection();
        list.DoubleClick += async (_, _) =>
        {
            if (sidebarPivot.SelectedIndex == HistoryTab && list.SelectedItem is ListEntry { Tag: HistoryEntry })
                await RunAsync();
        };
        pivot.SelectedIndexChanged += (_, _) => ShowPage();
        presetsButton.Click += (_, _) => ShowPresetsMenu();
        itemHeader.RunButton.Click += async (_, _) => await RunAsync();
        itemHeader.CancelButton.Click += (_, _) => callCts?.Cancel();
        argumentEditor.SubmitRequested += async (_, _) => await RunAsync();
        formatButton.Click += (_, _) => FormatRaw();
        fromFormButton.Click += (_, _) => rawBox.Value = Pretty(argumentEditor.GetFilledArguments());
        rawBox.TextBox.TextChanged += (_, _) => rawBox.HasError = !TryParseRaw(out _);
        resultHeader.WrapButton.Click += (_, _) =>
        {
            resultView.WordWrap = !resultView.WordWrap;
            resultHeader.WrapButton.Checked = resultView.WordWrap;
        };
        resultHeader.CopyButton.Click += (_, _) => CopyResult();
        elapsedTimer.Tick += (_, _) =>
        {
            if (runningId is not null && currentItem?.Id == runningId)
                resultHeader.SetStatus(ResultStatus.Running, Elapsed());
        };
        copiedTimer.Tick += (_, _) =>
        {
            copiedTimer.Stop();
            resultHeader.CopyButton.Text = "Copy";
            resultHeader.CopyButton.Glyph = Glyphs.Copy;
        };

        UpdateHeadersButton();
        SetState(ConnectionState.Disconnected);
        RefreshList();
        ShowPage();
        RestoreWindow();
    }

    private int S(int value) => LogicalToDeviceUnits(value);

    private static string Pretty(JsonNode node) => node.ToJsonString(JsonText.Pretty).ReplaceLineEndings("\r\n");

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
            mainSplit.Panel1MinSize = S(290);
            mainSplit.Panel2MinSize = S(480);
            mainSplit.SplitterDistance = settings.SidebarWidth > 0 ? Math.Clamp(settings.SidebarWidth, S(290), S(560)) : S(320);
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
                _ = RefreshItemsAsync();
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
            var headers = HeaderStore.Get(url);
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "MCP Client",
                AdditionalHeaders = headers.Count > 0 ? headers : null,
            });
            newClient = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "mcp-client-ui", Version = "1.0" },
            }, cancellationToken: timeout.Token);
            var (newTools, newResources, newPrompts) = await LoadItemsAsync(newClient, timeout.Token);

            client = newClient;
            connectedUrl = url;
            (tools, resources, prompts) = (newTools, newResources, newPrompts);
            currentItem = null;
            results.Clear();
            UpdateServerItem();

            settings.RememberUrl(url);
            settings.Save();
            if (!urlBox.TextBox.AutoCompleteCustomSource.Contains(url))
                urlBox.TextBox.AutoCompleteCustomSource.Add(url);

            SetState(ConnectionState.Connected);
            RefreshList();
        }
        catch (Exception ex)
        {
            if (newClient is not null) await SafeDisposeAsync(newClient);
            connectError = ex is OperationCanceledException ? "The server didn't answer within 20 seconds." : DescribeError(ex);
            SetState(ConnectionState.Failed);
        }
    }

    private static string DescribeError(Exception ex)
    {
        string message = ex.Message;
        if (message.Contains("401") || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            message += "\n\nThe server wants credentials: add a Bearer token or API key header with the Headers button.";
        return message;
    }

    /// <summary>Lists tools, resources (and templates) and prompts, each only if the server offers it.</summary>
    private static async Task<(List<McpItem>, List<McpItem>, List<McpItem>)> LoadItemsAsync(McpClient mcpClient, CancellationToken cancellationToken)
    {
        var caps = mcpClient.ServerCapabilities;
        var newTools = new List<McpItem>();
        var newResources = new List<McpItem>();
        var newPrompts = new List<McpItem>();

        if (caps?.Tools is not null)
            newTools.AddRange((await mcpClient.ListToolsAsync(cancellationToken: cancellationToken)).Select(McpItem.FromTool));
        if (caps?.Resources is not null)
        {
            newResources.AddRange((await mcpClient.ListResourcesAsync(cancellationToken: cancellationToken)).Select(McpItem.FromResource));
            try
            {
                newResources.AddRange((await mcpClient.ListResourceTemplatesAsync(cancellationToken: cancellationToken)).Select(McpItem.FromTemplate));
            }
            catch (McpException)
            {
                // Some servers list resources but not templates.
            }
        }
        if (caps?.Prompts is not null)
            newPrompts.AddRange((await mcpClient.ListPromptsAsync(cancellationToken: cancellationToken)).Select(McpItem.FromPrompt));

        static List<McpItem> Sorted(List<McpItem> items) => [.. items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
        return (Sorted(newTools), Sorted(newResources), Sorted(newPrompts));
    }

    private async Task DisconnectAsync()
    {
        callCts?.Cancel();
        var old = client;
        client = null;
        tools = [];
        resources = [];
        prompts = [];
        currentItem = null;
        results.Clear();
        SetState(ConnectionState.Disconnected);
        RefreshList();
        if (old is not null) await SafeDisposeAsync(old);
    }

    private static async Task SafeDisposeAsync(McpClient mcpClient)
    {
        try { await mcpClient.DisposeAsync(); }
        catch (Exception) { /* The server may already be gone. */ }
    }

    private async Task RefreshItemsAsync()
    {
        if (client is null || !refreshButton.Enabled) return;
        refreshButton.Enabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            (tools, resources, prompts) = await LoadItemsAsync(client, timeout.Token);
            var previous = currentItem;
            currentItem = null;
            UpdateServerItem();
            if (previous is not null && AllItems().FirstOrDefault(i => i.Id == previous.Id) is { } reloaded)
                SelectItem(reloaded);
            else
                UpdateRightPane();
            RefreshList();
        }
        catch (Exception ex)
        {
            statusPill.Set(ConnectionState.Failed, "Couldn't reload");
            toolTip.SetToolTip(statusPill, ex.Message);
        }
        finally
        {
            refreshButton.Enabled = client is not null;
        }
    }

    private IEnumerable<McpItem> AllItems() => tools.Concat(resources).Concat(prompts);

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
        headersButton.Enabled = newState != ConnectionState.Connecting;
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
        var parts = new List<string> { $"Version {client.ServerInfo.Version}", Plural(tools.Count, "tool") };
        if (resources.Count > 0) parts.Add(Plural(resources.Count, "resource"));
        if (prompts.Count > 0) parts.Add(Plural(prompts.Count, "prompt"));
        serverItem.Set(ServerName(client), string.Join(" · ", parts));
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    // ---------------------------------------------------------------- headers

    private void UpdateHeadersButton()
    {
        int count = HeaderStore.Get(urlBox.Value.Trim()).Count;
        headersButton.Text = count == 0 ? "Headers" : $"Headers ({count})";
        headersButton.Parent?.PerformLayout();
    }

    private async Task EditHeadersAsync()
    {
        string url = urlBox.Value.Trim();
        if (url.Length == 0)
        {
            urlBox.HasError = true;
            return;
        }
        using var dialog = new HeadersDialog(url, HeaderStore.Get(url));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        HeaderStore.Set(url, dialog.Headers);
        UpdateHeadersButton();

        // Headers go into the transport, so a live connection has to be made again.
        if (client is not null && string.Equals(connectedUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            await DisconnectAsync();
            await ToggleConnectionAsync();
        }
        else if (client is null && state == ConnectionState.Failed)
        {
            await ToggleConnectionAsync();
        }
    }

    // ---------------------------------------------------------------- sidebar

    private void RefreshList()
    {
        int tab = sidebarPivot.SelectedIndex;
        string query = searchBox.Value.Trim();
        bool Matches(string? text) => text?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;

        refreshButton.Visible = tab != HistoryTab;
        clearHistoryButton.Visible = tab == HistoryTab;
        listHeader.PerformLayout();

        suppressSelection = true;
        if (tab == HistoryTab)
        {
            var all = client is null ? [] : history.ForServer(connectedUrl).ToList();
            var visible = all.Where(h => query.Length == 0 || Matches(h.Name) || Matches(h.Arguments)).ToList();
            list.SetEntries(visible.Select(HistoryRow));
            listHeader.Title = "HISTORY";
            listHeader.Count = all.Count == 0 ? "" : query.Length == 0 ? $"{all.Count:N0}" : $"{visible.Count:N0} of {all.Count:N0}";
            clearHistoryButton.Enabled = all.Count > 0;
            list.EmptyText = client is null ? "Connect to a server to see its history."
                : all.Count == 0 ? "Calls you make show up here. Double-click one to run it again."
                : "Nothing matches your search.";
        }
        else
        {
            var (items, title, noun, offered) = tab switch
            {
                ResourcesTab => (resources, "RESOURCES", "resources", client?.ServerCapabilities?.Resources is not null),
                PromptsTab => (prompts, "PROMPTS", "prompts", client?.ServerCapabilities?.Prompts is not null),
                _ => (tools, "TOOLS", "tools", client?.ServerCapabilities?.Tools is not null),
            };
            var visible = items.Where(i => query.Length == 0 || Matches(i.Name) || Matches(i.Title) || Matches(i.Description) || Matches(i.Key)).ToList();
            list.SetEntries(visible.Select(i => new ListEntry(i.Title ?? i.Name, i.Subtitle, i)), e => ReferenceEquals(e.Tag, currentItem));
            listHeader.Title = title;
            listHeader.Count = items.Count == 0 ? "" : query.Length == 0 ? $"{items.Count:N0}" : $"{visible.Count:N0} of {items.Count:N0}";
            list.EmptyText = client is null ? $"Connect to a server to see its {noun}."
                : !offered ? $"This server doesn't offer {noun}."
                : items.Count == 0 ? $"This server has no {noun}."
                : "Nothing matches your search.";
        }
        suppressSelection = false;
    }

    private static ListEntry HistoryRow(HistoryEntry entry)
    {
        string arguments = "No arguments";
        try
        {
            if (JsonNode.Parse(entry.Arguments) is JsonObject { Count: > 0 } args)
                arguments = string.Join(", ", args.Select(kv => $"{kv.Key}: {kv.Value?.ToJsonString() ?? "null"}"));
        }
        catch (JsonException)
        {
            arguments = entry.Arguments;
        }
        var local = entry.Time.ToLocalTime();
        string time = local.Date == DateTime.Today ? local.ToString("t") : local.ToString("g");
        return new ListEntry(entry.Name, arguments, entry, StatusColor(entry.Status), time);
    }

    private static Color StatusColor(ResultStatus status) => status switch
    {
        ResultStatus.Success => Theme.Success,
        ResultStatus.ToolError or ResultStatus.Failed => Theme.Danger,
        _ => Theme.TertiaryText,
    };

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down when list.Items.Count > 0:
                list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                e.SuppressKeyPress = true;
                break;
            case Keys.Up when list.Items.Count > 0:
                list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
                e.SuppressKeyPress = true;
                break;
            case Keys.Enter when list.Items.Count > 0:
                if (list.SelectedIndex < 0) list.SelectedIndex = 0;
                if (argumentEditor.HasFields && pivot.SelectedIndex == 0) argumentEditor.FocusFirst();
                else itemHeader.RunButton.Focus();
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
        if (suppressSelection || list.SelectedItem is not ListEntry entry) return;
        switch (entry.Tag)
        {
            case McpItem item when !ReferenceEquals(item, currentItem):
                SelectItem(item);
                break;
            case HistoryEntry past:
                OpenHistory(past);
                break;
        }
    }

    private void ShowOverview()
    {
        if (client is null) return;
        currentItem = null;
        if (sidebarPivot.SelectedIndex != HistoryTab)
        {
            suppressSelection = true;
            list.ClearSelected();
            suppressSelection = false;
        }
        UpdateRunButtons();
        UpdateRightPane();
    }

    private void SelectItem(McpItem item)
    {
        currentItem = item;
        itemHeader.SetItem(item);
        argumentEditor.Load(item.Schema);
        schemaView.ShowJson(JsonText.Format(item.Schema));
        rawBox.Value = BuildRawTemplate(item.Schema);
        resultHeader.Title = item.ResultTitle;
        workSplit.Panel1Collapsed = item.Kind == ItemKind.Resource;
        ShowResult();
        UpdateRunButtons();
        UpdateRightPane();
        FitArgumentsCard();
    }

    /// <summary>Selects the call's item, puts its arguments back and shows its stored result.</summary>
    private void OpenHistory(HistoryEntry past)
    {
        var item = AllItems().FirstOrDefault(i => i.Kind == past.Kind && i.Key == past.Key);
        if (item is null)
        {
            MessageBox.Show(this, $"{past.Name} is no longer offered by this server.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ReferenceEquals(item, currentItem))
            SelectItem(item);
        ApplyArguments(past.Arguments);
        var local = past.Time.ToLocalTime();
        results[item.Id] = new ResultState(past.Status, $"{past.Meta} · {local:g}", past.Text);
        ShowResult();
    }

    private void ApplyArguments(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject args) return;
        rawBox.Value = Pretty(args);
        bool fits = argumentEditor.SetValues((JsonObject)args.DeepClone());
        if (!fits) pivot.SelectedIndex = 1;
        else if (pivot.SelectedIndex == 1) pivot.SelectedIndex = 0;
    }

    private void ClearHistory()
    {
        if (client is null) return;
        if (MessageBox.Show(this, "Clear the call history for this server?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        history.Clear(connectedUrl);
        RefreshList();
    }

    private void UpdateRightPane()
    {
        serverItem.Selected = client is not null && currentItem is null;
        if (client is not null && currentItem is not null)
        {
            emptyState.Visible = false;
            itemView.Visible = true;
            EnsureWorkSplit();
            return;
        }

        itemView.Visible = false;
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
                if (client.ServerCapabilities?.Resources is not null) chips.Add(Plural(resources.Count, "resource"));
                if (client.ServerCapabilities?.Prompts is not null) chips.Add(Plural(prompts.Count, "prompt"));
                emptyState.Show(Glyphs.Globe, ServerName(client), instructions + "\n\nPick something on the left to try it.", Theme.Accent, [.. chips]);
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
                    "Enter the server's Streamable HTTP address above and press Connect. Its tools, resources and prompts appear on the left, and you can call any of them with your own arguments.",
                    Theme.Accent);
                break;
        }
    }

    private void EnsureWorkSplit()
    {
        if (workSplitReady) return;
        itemView.PerformLayout();
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
        if (!workSplitReady || workSplit.Panel1Collapsed) return;
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
        presetsButton.Visible = page != 2;
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
        return Pretty(template);
    }

    private bool TryParseRaw(out JsonObject arguments)
    {
        arguments = [];
        string text = rawBox.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            rawStatus.Set(Glyphs.Info, "Empty: called without arguments", Theme.SecondaryText);
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
            rawBox.Value = Pretty(parsed);
    }

    // ---------------------------------------------------------------- saved argument values

    private void ShowPresetsMenu()
    {
        var item = currentItem;
        if (item is null) return;
        string key = AppSettings.PresetKey(connectedUrl, item);
        settings.Presets.TryGetValue(key, out var saved);

        var menu = new ContextMenuStrip { ShowImageMargin = false, Font = Theme.Body };
        menu.Items.Add("Save current values…", null, (_, _) => SavePreset(item));
        if (saved is { Count: > 0 })
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var (name, json) in saved.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var load = new ToolStripMenuItem(name, null, (_, _) => ApplyArguments(json)) { ToolTipText = json };
                menu.Items.Add(load);
            }
            menu.Items.Add(new ToolStripSeparator());
            var delete = new ToolStripMenuItem("Delete");
            foreach (var name in saved.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                delete.DropDownItems.Add(name, null, (_, _) => DeletePreset(key, name));
            menu.Items.Add(delete);
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(presetsButton, new Point(presetsButton.Width - menu.PreferredSize.Width, presetsButton.Height));
    }

    private void SavePreset(McpItem item)
    {
        JsonObject args;
        if (pivot.SelectedIndex == 1)
        {
            if (!TryParseRaw(out args))
            {
                rawBox.HasError = true;
                return;
            }
        }
        else
        {
            args = argumentEditor.GetFilledArguments();
        }

        using var dialog = new NameDialog("Save values", $"Name for these {item.Name} values:", "");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string key = AppSettings.PresetKey(connectedUrl, item);
        if (!settings.Presets.TryGetValue(key, out var saved))
            settings.Presets[key] = saved = [];
        if (saved.ContainsKey(dialog.Value) &&
            MessageBox.Show(this, $"Replace the saved values \"{dialog.Value}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        saved[dialog.Value] = args.ToJsonString();
        settings.Save();
    }

    private void DeletePreset(string key, string name)
    {
        if (!settings.Presets.TryGetValue(key, out var saved)) return;
        saved.Remove(name);
        if (saved.Count == 0) settings.Presets.Remove(key);
        settings.Save();
    }

    // ---------------------------------------------------------------- calling

    private void UpdateRunButtons()
    {
        bool running = callCts is not null;
        itemHeader.RunButton.Enabled = client is not null && !running;
        itemHeader.CancelButton.Visible = running && currentItem is not null && currentItem.Id == runningId;
    }

    private async Task RunAsync()
    {
        var item = currentItem;
        var mcpClient = client;
        if (item is null || mcpClient is null || callCts is not null) return;

        JsonObject arguments = [];
        if (item.Kind != ItemKind.Resource)
        {
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
        }
        var callArguments = arguments.ToDictionary(
            kv => kv.Key,
            kv => (object?)(kv.Value is null ? null : JsonSerializer.SerializeToElement(kv.Value)));

        var cts = new CancellationTokenSource();
        callCts = cts;
        runningId = item.Id;
        results.Remove(item.Id);
        callWatch.Restart();
        elapsedTimer.Start();
        ShowResult();
        UpdateRunButtons();

        ResultState outcome;
        try
        {
            ResultStatus status = ResultStatus.Success;
            string text;
            switch (item.Kind)
            {
                case ItemKind.Tool:
                    var result = await mcpClient.CallToolAsync(item.Key, callArguments, cancellationToken: cts.Token);
                    text = FormatResult(result);
                    if (result.IsError == true) status = ResultStatus.ToolError;
                    break;
                case ItemKind.Resource:
                    text = FormatResource(await mcpClient.ReadResourceAsync(item.Key, cancellationToken: cts.Token));
                    break;
                case ItemKind.ResourceTemplate:
                    text = FormatResource(await mcpClient.ReadResourceAsync(item.Key, callArguments, cancellationToken: cts.Token));
                    break;
                default:
                    text = FormatPrompt(await mcpClient.GetPromptAsync(item.Key, callArguments, cancellationToken: cts.Token));
                    break;
            }
            callWatch.Stop();
            outcome = new ResultState(status, Meta(text), text);
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
            runningId = null;
            cts.Dispose();
        }

        if (ReferenceEquals(client, mcpClient))
        {
            results[item.Id] = outcome;
            history.Add(new HistoryEntry
            {
                Url = connectedUrl,
                Kind = item.Kind,
                Key = item.Key,
                Name = item.Name,
                Arguments = arguments.ToJsonString(),
                Status = outcome.Status,
                Meta = outcome.Meta,
                Text = outcome.Text,
                Time = DateTime.UtcNow,
            });
            if (sidebarPivot.SelectedIndex == HistoryTab)
                RefreshList();
        }
        if (currentItem?.Id == item.Id)
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
        var item = currentItem;
        bool running = item is not null && runningId == item.Id;
        progress.Running = running;
        if (running)
        {
            resultHeader.SetStatus(ResultStatus.Running, Elapsed());
            resultView.ShowPlaceholder("Waiting for the server…");
        }
        else if (item is not null && results.TryGetValue(item.Id, out var result))
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
            resultView.ShowPlaceholder(item?.Kind switch
            {
                ItemKind.Resource or ItemKind.ResourceTemplate => "Press Read to see the resource's contents.",
                ItemKind.Prompt => "Press Get to see the prompt's messages.",
                _ => "Run the tool to see its result here.",
            });
        }
        resultHeader.CopyButton.Enabled = resultView.Content.Length > 0;
    }

    private static string FormatResult(CallToolResult result)
    {
        var parts = new List<string>();
        foreach (var block in result.Content)
            parts.Add(FormatBlock(block));
        if (parts.Count == 0 && result.StructuredContent is { } structured)
            parts.Add(JsonText.Format(structured));
        return parts.Count == 0 ? "(The tool returned no content.)" : string.Join("\n\n", parts);
    }

    private static string FormatResource(ReadResourceResult result)
    {
        bool several = result.Contents.Count > 1;
        var parts = result.Contents.Select(content =>
        {
            string body = content switch
            {
                TextResourceContents text => JsonText.TryFormat(text.Text, out var pretty) ? pretty : text.Text,
                BlobResourceContents blob => $"[binary {blob.MimeType ?? "data"}, {blob.DecodedData.Length:N0} bytes]",
                _ => content.ToString() ?? "",
            };
            return several ? $"── {content.Uri} ({content.MimeType ?? "unknown type"}) ──\n{body}" : body;
        }).ToList();
        return parts.Count == 0 ? "(The resource is empty.)" : string.Join("\n\n", parts);
    }

    private static string FormatPrompt(GetPromptResult result)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.Description))
            text.Append(result.Description.Trim()).Append("\n\n");
        foreach (var message in result.Messages)
            text.Append("── ").Append(message.Role.ToString().ToLowerInvariant()).Append(" ──\n").Append(FormatBlock(message.Content)).Append("\n\n");
        return text.Length == 0 ? "(The prompt has no messages.)" : text.ToString().TrimEnd();
    }

    private static string FormatBlock(ContentBlock block) => block switch
    {
        TextContentBlock text => JsonText.TryFormat(text.Text, out var pretty) ? pretty : text.Text,
        ImageContentBlock image => $"[image: {image.MimeType}, {image.DecodedData.Length:N0} bytes]",
        _ => DescribeBlock(block),
    };

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
