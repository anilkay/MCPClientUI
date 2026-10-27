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
        urlBox.Placeholder = "http://localhost:5199/mcp  or a stdio command, e.g. npx -y @modelcontextprotocol/server-everything";
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
        var target = ServerTarget.Parse(url);
        if (target is null)
        {
            urlBox.HasError = true;
            connectError = url.Length == 0
                ? "Enter an http:// or https:// URL, or a command that starts a stdio server."
                : "That isn't a valid URL. Use http:// or https://, for example http://localhost:5199/mcp.";
            SetState(ConnectionState.Failed);
            urlBox.TextBox.Focus();
            return;
        }

        connectError = null;
        SetState(ConnectionState.Connecting);
        McpClient? newClient = null;
        var stderr = new StderrTail();
        var wait = McpOperations.ConnectTimeout(target);
        try
        {
            using var timeout = new CancellationTokenSource(wait);
            var transport = McpOperations.CreateTransport(target, HeaderStore.Get(url), stderr);
            newClient = await McpOperations.ConnectAsync(transport, timeout.Token);
            var items = await McpOperations.LoadItemsAsync(newClient, timeout.Token);

            client = newClient;
            connectedUrl = url;
            (tools, resources, prompts) = (items.Tools, items.Resources, items.Prompts);
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
            connectError = McpOperations.DescribeConnectError(ex, target, wait, stderr);
            SetState(ConnectionState.Failed);
        }
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
            var items = await McpOperations.LoadItemsAsync(client, timeout.Token);
            (tools, resources, prompts) = (items.Tools, items.Resources, items.Prompts);
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
        string serverName = client is null ? "" : McpOperations.ServerName(client);
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

    private void UpdateServerItem()
    {
        if (client is null) return;
        var parts = new List<string> { $"Version {client.ServerInfo.Version}", Plural(tools.Count, "tool") };
        if (resources.Count > 0) parts.Add(Plural(resources.Count, "resource"));
        if (prompts.Count > 0) parts.Add(Plural(prompts.Count, "prompt"));
        serverItem.Set(McpOperations.ServerName(client), string.Join(" · ", parts));
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    // ---------------------------------------------------------------- headers

    private void UpdateHeadersButton()
    {
        string url = urlBox.Value.Trim();
        int count = HeaderStore.Get(url).Count;
        string label = ServerTarget.Parse(url)?.IsStdio == true ? "Environment" : "Headers";
        headersButton.Text = count == 0 ? label : $"{label} ({count})";
        toolTip.SetToolTip(headersButton, label == "Headers"
            ? "Bearer token and other HTTP headers for this server"
            : "Environment variables (API keys, settings) for this server's process");
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
        Dictionary<string, string> values;
        if (ServerTarget.Parse(url)?.IsStdio == true)
        {
            using var dialog = new EnvironmentDialog(url, HeaderStore.Get(url));
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            values = dialog.Variables;
        }
        else
        {
            using var dialog = new HeadersDialog(url, HeaderStore.Get(url));
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            values = dialog.Headers;
        }
        HeaderStore.Set(url, values);
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
                emptyState.Show(Glyphs.Globe, McpOperations.ServerName(client), instructions + "\n\nPick something on the left to try it.", Theme.Accent, [.. chips]);
                break;
            case ConnectionState.Connecting:
                emptyState.Show(Glyphs.Plug, "Connecting…", urlBox.Value.Trim(), Theme.Accent);
                break;
            case ConnectionState.Failed:
                emptyState.Show(Glyphs.Error, "Couldn't connect",
                    (connectError ?? "") + (ServerTarget.Parse(urlBox.Value)?.IsStdio == true
                        ? "\n\nCheck the command, and that it starts an MCP server that talks over stdin/stdout."
                        : "\n\nCheck that the server is running and that the URL is right."), Theme.Danger);
                break;
            default:
                emptyState.Show(Glyphs.Plug, "Connect to an MCP server",
                    "Enter the server's Streamable HTTP address above, or the command that starts a stdio server (for example npx -y @modelcontextprotocol/server-everything), and press Connect. Its tools, resources and prompts appear on the left, and you can call any of them with your own arguments.",
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
    private static string BuildRawTemplate(JsonElement schema) => Pretty(McpOperations.RawTemplate(schema));

    private bool TryParseRaw(out JsonObject arguments)
    {
        bool ok = McpOperations.TryParseArguments(rawBox.Value, out arguments, out var message);
        if (!ok) rawStatus.Set(Glyphs.Error, message, Theme.Danger);
        else if (string.IsNullOrWhiteSpace(rawBox.Value)) rawStatus.Set(Glyphs.Info, message, Theme.SecondaryText);
        else rawStatus.Set(Glyphs.Check, message, Theme.Success);
        return ok;
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
            var result = await McpOperations.RunAsync(mcpClient, item, arguments, cts.Token);
            callWatch.Stop();
            outcome = new ResultState(result.Status, Meta(result.Text), result.Text);
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
