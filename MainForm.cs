using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpClientUI;

/// <summary>
/// Minimal MCP client: connect to a Streamable HTTP server, list its tools, fill in
/// arguments and call them.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly TextBox urlBox = new() { Text = "http://localhost:5199/mcp", Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly Button connectButton = new() { Text = "Connect", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 0, 6, 0) };
    private readonly Label statusLabel = new() { Text = "Not connected.", AutoSize = true, Anchor = AnchorStyles.Left };

    private readonly ListView toolList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        ShowItemToolTips = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };

    private readonly Label toolTitle = new() { Dock = DockStyle.Top, AutoSize = false, Height = 30, Font = new Font("Segoe UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox toolDescription = new() { Dock = DockStyle.Top, Multiline = true, ReadOnly = true, Height = 70, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, TabStop = false };
    private readonly ArgumentEditor argumentEditor = new() { Dock = DockStyle.Fill };
    private readonly TextBox rawArgsBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Cascadia Mono", 9.5f) };
    private readonly CheckBox useRawArgs = new() { Text = "Send this raw JSON instead of the form", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
    private readonly TextBox schemaBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Cascadia Mono", 9.5f) };
    private readonly TabControl argTabs = new() { Dock = DockStyle.Fill };
    private readonly Button callButton = new() { Text = "Call tool", AutoSize = true, Enabled = false };
    private readonly Button cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Label callStatus = new() { AutoSize = true, Margin = new Padding(8, 8, 3, 3) };
    private readonly TextBox resultBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Font = new Font("Cascadia Mono", 9.5f) };

    private McpClient? client;
    private CancellationTokenSource? callCts;

    public MainForm()
    {
        Text = "MCP Client";
        Size = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        // Top bar: URL + connect + status.
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 4, RowCount = 1, Padding = new Padding(6, 4, 6, 4) };
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        top.Controls.Add(new Label { Text = "Server URL:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.Controls.Add(urlBox, 1, 0);
        top.Controls.Add(connectButton, 2, 0);
        top.Controls.Add(statusLabel, 3, 0);

        // Left: tool list.
        toolList.Columns.Add("Tools", 260);
        toolList.Resize += (_, _) => toolList.Columns[0].Width = Math.Max(100, toolList.ClientSize.Width - 4);

        // Right: tool details, arguments, result.
        var argsPage = new TabPage("Arguments");
        argsPage.Controls.Add(argumentEditor);
        var rawPage = new TabPage("Raw JSON");
        rawPage.Controls.Add(rawArgsBox);
        rawPage.Controls.Add(useRawArgs);
        var schemaPage = new TabPage("Input schema");
        schemaPage.Controls.Add(schemaBox);
        argTabs.TabPages.AddRange([argsPage, rawPage, schemaPage]);

        var callBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(2) };
        callBar.Controls.AddRange([callButton, cancelButton, callStatus]);

        // Fill first, then Top/Bottom (WinForms docks in reverse z-order).
        var upper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        upper.Controls.Add(argTabs);
        upper.Controls.Add(callBar);
        upper.Controls.Add(toolDescription);
        upper.Controls.Add(toolTitle);

        var resultGroup = new GroupBox { Text = "Result", Dock = DockStyle.Fill, Padding = new Padding(6) };
        resultGroup.Controls.Add(resultBox);

        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        rightSplit.Panel1.Controls.Add(upper);
        rightSplit.Panel2.Controls.Add(resultGroup);

        var mainSplit = new SplitContainer { Dock = DockStyle.Fill };
        mainSplit.Panel1.Controls.Add(toolList);
        mainSplit.Panel2.Controls.Add(rightSplit);

        Controls.Add(mainSplit);
        Controls.Add(top);

        Load += (_, _) =>
        {
            mainSplit.SplitterDistance = 300;
            rightSplit.SplitterDistance = (int)(rightSplit.Height * 0.55);
        };

        AcceptButton = connectButton;
        connectButton.Click += async (_, _) => await ConnectAsync();
        toolList.SelectedIndexChanged += (_, _) => ShowSelectedTool();
        callButton.Click += async (_, _) => await CallToolAsync();
        cancelButton.Click += (_, _) => callCts?.Cancel();
        FormClosing += async (_, _) => await DisconnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (client is not null)
        {
            await DisconnectAsync();
            return;
        }

        if (!Uri.TryCreate(urlBox.Text.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(this, "Enter an http:// or https:// URL.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        connectButton.Enabled = false;
        statusLabel.Text = "Connecting...";
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "MCP Client UI",
            });
            client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "mcp-client-ui", Version = "1.0" },
            });

            var tools = await client.ListToolsAsync();
            toolList.BeginUpdate();
            toolList.Items.Clear();
            foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                toolList.Items.Add(new ListViewItem(tool.Name) { Tag = tool, ToolTipText = tool.Description });
            toolList.EndUpdate();

            var info = client.ServerInfo;
            statusLabel.Text = $"Connected to {info.Title ?? info.Name} {info.Version}: {tools.Count} tools";
            connectButton.Text = "Disconnect";
            urlBox.ReadOnly = true;
            AcceptButton = null;

            if (!string.IsNullOrWhiteSpace(client.ServerInstructions))
                resultBox.Text = ("Server instructions:\n" + client.ServerInstructions).ReplaceLineEndings(Environment.NewLine);
            if (toolList.Items.Count > 0)
                toolList.Items[0].Selected = true;
        }
        catch (Exception ex)
        {
            await DisconnectAsync();
            statusLabel.Text = "Connection failed.";
            MessageBox.Show(this, ex.Message, "Connection failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            connectButton.Enabled = true;
        }
    }

    private async Task DisconnectAsync()
    {
        callCts?.Cancel();
        var c = client;
        client = null;
        toolList.Items.Clear();
        ShowSelectedTool();
        connectButton.Text = "Connect";
        urlBox.ReadOnly = false;
        AcceptButton = connectButton;
        statusLabel.Text = "Not connected.";
        if (c is not null)
        {
            try { await c.DisposeAsync(); } catch { /* server may already be gone */ }
        }
    }

    private McpClientTool? SelectedTool =>
        toolList.SelectedItems.Count == 1 ? toolList.SelectedItems[0].Tag as McpClientTool : null;

    private void ShowSelectedTool()
    {
        var tool = SelectedTool;
        callButton.Enabled = tool is not null && callCts is null;
        if (tool is null)
        {
            toolTitle.Text = "";
            toolDescription.Text = "";
            schemaBox.Text = "";
            rawArgsBox.Text = "";
            argumentEditor.Clear();
            return;
        }

        var pt = tool.ProtocolTool;
        toolTitle.Text = string.IsNullOrEmpty(pt.Title) || pt.Title == pt.Name ? pt.Name : $"{pt.Title}  ({pt.Name})";

        var desc = new StringBuilder(pt.Description ?? "(no description)");
        if (pt.Annotations is { } a)
        {
            var flags = new List<string>();
            if (a.ReadOnlyHint == true) flags.Add("read-only");
            if (a.DestructiveHint == true) flags.Add("destructive");
            if (a.IdempotentHint == true) flags.Add("idempotent");
            if (a.OpenWorldHint == true) flags.Add("open-world");
            if (flags.Count > 0) desc.Append("\n[").Append(string.Join(", ", flags)).Append(']');
        }
        toolDescription.Text = desc.ToString().ReplaceLineEndings(Environment.NewLine);

        schemaBox.Text = JsonSerializer.Serialize(pt.InputSchema, Indented).ReplaceLineEndings(Environment.NewLine);
        argumentEditor.Load(pt.InputSchema);
        rawArgsBox.Text = BuildRawTemplate(pt.InputSchema).ReplaceLineEndings(Environment.NewLine);
        useRawArgs.Checked = false;
    }

    /// <summary>A starting JSON object with every property set to an empty value of its type.</summary>
    private static string BuildRawTemplate(JsonElement schema)
    {
        var obj = new JsonObject();
        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in props.EnumerateObject())
            {
                string type = p.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "string";
                obj[p.Name] = type switch
                {
                    "integer" or "number" => 0,
                    "boolean" => false,
                    "array" => new JsonArray(),
                    "object" => new JsonObject(),
                    _ => "",
                };
            }
        }
        return obj.ToJsonString(Indented);
    }

    private async Task CallToolAsync()
    {
        var tool = SelectedTool;
        if (tool is null || client is null) return;

        Dictionary<string, object?> args;
        try
        {
            JsonObject obj = useRawArgs.Checked
                ? JsonNode.Parse(string.IsNullOrWhiteSpace(rawArgsBox.Text) ? "{}" : rawArgsBox.Text) as JsonObject
                    ?? throw new FormatException("Raw arguments must be a JSON object.")
                : argumentEditor.GetArguments();
            args = obj.ToDictionary(kv => kv.Key, kv => (object?)(kv.Value is null ? null : JsonSerializer.SerializeToElement(kv.Value)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "Invalid arguments", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        callCts = new CancellationTokenSource();
        callButton.Enabled = false;
        cancelButton.Enabled = true;
        callStatus.Text = $"Calling {tool.Name}...";
        resultBox.Text = "";
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await client.CallToolAsync(tool.Name, args, cancellationToken: callCts.Token);
            callStatus.Text = $"{(result.IsError == true ? "Tool returned an error" : "Done")} in {sw.Elapsed.TotalSeconds:0.0} s";
            resultBox.Text = FormatResult(result).ReplaceLineEndings(Environment.NewLine);
        }
        catch (OperationCanceledException)
        {
            callStatus.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            callStatus.Text = $"Failed after {sw.Elapsed.TotalSeconds:0.0} s";
            resultBox.Text = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            callCts.Dispose();
            callCts = null;
            cancelButton.Enabled = false;
            callButton.Enabled = SelectedTool is not null && client is not null;
        }
    }

    private static string FormatResult(CallToolResult result)
    {
        var sb = new StringBuilder();
        if (result.IsError == true) sb.AppendLine("[isError: true]").AppendLine();

        foreach (var block in result.Content)
        {
            switch (block)
            {
                case TextContentBlock text:
                    sb.AppendLine(text.Text);
                    break;
                case ImageContentBlock image:
                    sb.AppendLine($"[image {image.MimeType}, {image.DecodedData.Length:N0} bytes]");
                    break;
                default:
                    sb.AppendLine(JsonSerializer.Serialize(block, Indented));
                    break;
            }
        }

        if (result.StructuredContent is { } structured)
        {
            sb.AppendLine().AppendLine("Structured content:");
            sb.AppendLine(JsonSerializer.Serialize(structured, Indented));
        }

        return sb.Length == 0 ? "(empty result)" : sb.ToString();
    }
}
