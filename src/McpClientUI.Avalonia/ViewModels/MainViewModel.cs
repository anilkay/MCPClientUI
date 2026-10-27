using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelContextProtocol.Client;

namespace McpClientUI.Desktop.ViewModels;

/// <summary>
/// The main window: connection, the sidebar (tools, resources, prompts, history), the selected
/// item with its argument form, and the result. The MCP work itself is in McpClientUI.Core.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const int ToolsTab = 0, ResourcesTab = 1, PromptsTab = 2, HistoryTab = 3;
    public const int FormPage = 0, RawPage = 1, SchemaPage = 2;

    private sealed record StoredResult(ResultStatus Status, string Meta, string Text);

    private readonly IDialogs dialogs;
    private readonly AppSettings settings = AppSettings.Load();
    private readonly HistoryStore history = HistoryStore.Load();
    private readonly Dictionary<string, StoredResult> results = [];
    private readonly Stopwatch callWatch = new();
    private readonly DispatcherTimer elapsedTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private McpClient? client;
    private string connectedUrl = "";
    private ServerItems items = ServerItems.Empty;
    private CancellationTokenSource? callCts;
    private string? runningId;
    private bool suppressSelection;

    public MainViewModel(IDialogs dialogs)
    {
        this.dialogs = dialogs;
        url = settings.Url;
        RecentUrls = [.. settings.RecentUrls];
        elapsedTimer.Tick += (_, _) =>
        {
            if (runningId is not null && CurrentItem?.Id == runningId) ResultMeta = Elapsed();
        };
        UpdateSecretsButton();
        UpdateState(ConnectionState.Disconnected);
        RefreshList();
    }

    public ObservableCollection<string> RecentUrls { get; }
    public ObservableCollection<Entry> Entries { get; } = [];
    public ObservableCollection<FieldViewModel> Fields { get; } = [];
    public ObservableCollection<Chip> ItemChips { get; } = [];
    public ObservableCollection<Chip> OverviewChips { get; } = [];
    public ObservableCollection<string> PresetNames { get; } = [];

    // ------------------------------------------------------------ connection state

    [ObservableProperty] private string url;
    [ObservableProperty] private ConnectionState state;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string connectButtonText = "Connect";
    [ObservableProperty] private string secretsButtonText = "Headers";
    [ObservableProperty] private string? connectError;
    [ObservableProperty] private string serverTitle = "";
    [ObservableProperty] private string serverSubtitle = "";

    public bool IsConnected => State == ConnectionState.Connected;
    public bool IsConnecting => State == ConnectionState.Connecting;
    public bool IsNotConnecting => State != ConnectionState.Connecting;
    public bool UrlReadOnly => State is ConnectionState.Connected or ConnectionState.Connecting;
    public bool StatusOk => State == ConnectionState.Connected;
    public bool StatusFailed => State == ConnectionState.Failed;
    public bool StatusBusy => State == ConnectionState.Connecting;

    // ------------------------------------------------------------ sidebar

    [ObservableProperty] private int sidebarTab;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private Entry? selectedEntry;
    [ObservableProperty] private string sectionTitle = "TOOLS";
    [ObservableProperty] private string sectionCount = "";
    [ObservableProperty] private string emptyListText = "";
    [ObservableProperty] private bool isHistoryTab;

    // ------------------------------------------------------------ right side

    [ObservableProperty] private McpItem? currentItem;
    [ObservableProperty] private string emptyGlyph = "plug";
    [ObservableProperty] private string emptyHeading = "";
    [ObservableProperty] private string emptyBody = "";
    [ObservableProperty] private bool emptyIsError;
    [ObservableProperty] private string itemTitle = "";
    [ObservableProperty] private string itemDescription = "";
    [ObservableProperty] private string runText = "Run";
    [ObservableProperty] private bool hasArguments;
    [ObservableProperty] private bool showArgumentsCard;
    [ObservableProperty] private int argumentsPage;
    [ObservableProperty] private string rawJson = "";
    [ObservableProperty] private string rawStatus = "";
    [ObservableProperty] private bool rawOk = true;
    [ObservableProperty] private string schemaJson = "";
    [ObservableProperty] private string resultTitle = "Result";
    [ObservableProperty] private ResultStatus resultStatus;
    [ObservableProperty] private string resultMeta = "";
    [ObservableProperty] private string resultText = "";
    [ObservableProperty] private string resultPlaceholder = "";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool wrapResult = true;

    public bool ShowItem => client is not null && CurrentItem is not null;
    public bool ShowEmpty => !ShowItem;
    public bool HasResult => ResultText.Length > 0;
    public bool CanRun => client is not null && callCts is null;
    public bool ShowCancel => callCts is not null && CurrentItem?.Id == runningId;
    public bool ShowRawPage => ArgumentsPage == RawPage;
    public bool ShowSchemaPage => ArgumentsPage == SchemaPage;
    public bool ShowFormPage => ArgumentsPage == FormPage;
    public bool NoArguments => !HasArguments;
    public string ArgumentsHint => ArgumentsPage switch
    {
        FormPage => "Run sends these values · Ctrl+Enter",
        RawPage => "Run sends this JSON · Ctrl+Enter",
        _ => "",
    };
    public string ResultStatusText => ResultStatus switch
    {
        ResultStatus.Running => "Running",
        ResultStatus.Success => "Success",
        ResultStatus.ToolError => "Tool error",
        ResultStatus.Failed => "Failed",
        ResultStatus.Cancelled => "Cancelled",
        _ => "",
    };
    public bool HasResultStatus => ResultStatus != ResultStatus.None;
    public bool ResultIsSuccess => ResultStatus == ResultStatus.Success;
    public bool ResultIsError => ResultStatus is ResultStatus.ToolError or ResultStatus.Failed;
    public bool ResultIsNeutral => ResultStatus is ResultStatus.Running or ResultStatus.Cancelled;
    /// <summary>Failed calls show their message in red; JSON results get colors.</summary>
    public bool ResultIsFailure => ResultStatus == ResultStatus.Failed;

    partial void OnStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsNotConnecting));
        OnPropertyChanged(nameof(UrlReadOnly));
        OnPropertyChanged(nameof(StatusOk));
        OnPropertyChanged(nameof(StatusFailed));
        OnPropertyChanged(nameof(StatusBusy));
    }

    partial void OnUrlChanged(string value) => UpdateSecretsButton();
    partial void OnSidebarTabChanged(int value) => RefreshList();
    partial void OnSearchTextChanged(string value) => RefreshList();
    partial void OnCurrentItemChanged(McpItem? value) => NotifyItemView();
    partial void OnArgumentsPageChanged(int value)
    {
        OnPropertyChanged(nameof(ShowRawPage));
        OnPropertyChanged(nameof(ShowSchemaPage));
        OnPropertyChanged(nameof(ShowFormPage));
        OnPropertyChanged(nameof(ArgumentsHint));
    }
    partial void OnHasArgumentsChanged(bool value) => OnPropertyChanged(nameof(NoArguments));
    partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(HasResult));
    partial void OnRawJsonChanged(string value) => ValidateRaw();
    partial void OnResultStatusChanged(ResultStatus value)
    {
        OnPropertyChanged(nameof(ResultStatusText));
        OnPropertyChanged(nameof(HasResultStatus));
        OnPropertyChanged(nameof(ResultIsSuccess));
        OnPropertyChanged(nameof(ResultIsError));
        OnPropertyChanged(nameof(ResultIsNeutral));
        OnPropertyChanged(nameof(ResultIsFailure));
    }

    partial void OnSelectedEntryChanged(Entry? value)
    {
        if (suppressSelection || value is null) return;
        switch (value.Tag)
        {
            case McpItem item when !ReferenceEquals(item, CurrentItem):
                SelectItem(item);
                break;
            case HistoryEntry past:
                _ = OpenHistoryAsync(past);
                break;
        }
    }

    private void NotifyItemView()
    {
        OnPropertyChanged(nameof(ShowItem));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ShowCancel));
    }

    // ------------------------------------------------------------ connecting

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (client is not null)
        {
            await DisconnectAsync();
            return;
        }
        if (State == ConnectionState.Connecting) return;

        string target = (Url ?? "").Trim();
        var parsed = ServerTarget.Parse(target);
        if (parsed is null)
        {
            ConnectError = target.Length == 0
                ? "Enter an http:// or https:// URL, or a command that starts a stdio server."
                : "That isn't a valid URL. Use http:// or https://, for example http://localhost:5199/mcp.";
            UpdateState(ConnectionState.Failed);
            return;
        }

        ConnectError = null;
        UpdateState(ConnectionState.Connecting);
        McpClient? newClient = null;
        var stderr = new StderrTail();
        var wait = McpOperations.ConnectTimeout(parsed);
        try
        {
            using var timeout = new CancellationTokenSource(wait);
            var transport = McpOperations.CreateTransport(parsed, HeaderStore.Get(target), stderr);
            newClient = await McpOperations.ConnectAsync(transport, timeout.Token);
            var loaded = await McpOperations.LoadItemsAsync(newClient, timeout.Token);

            client = newClient;
            connectedUrl = target;
            items = loaded;
            CurrentItem = null;
            results.Clear();
            settings.RememberUrl(target);
            settings.Save();
            if (!RecentUrls.Contains(target)) RecentUrls.Insert(0, target);
            UpdateServerItem();
            UpdateState(ConnectionState.Connected);
            RefreshList();
        }
        catch (Exception ex)
        {
            if (newClient is not null) await SafeDisposeAsync(newClient);
            ConnectError = McpOperations.DescribeConnectError(ex, parsed, wait, stderr);
            UpdateState(ConnectionState.Failed);
        }
    }

    private async Task DisconnectAsync()
    {
        callCts?.Cancel();
        var old = client;
        client = null;
        items = ServerItems.Empty;
        CurrentItem = null;
        results.Clear();
        UpdateState(ConnectionState.Disconnected);
        RefreshList();
        if (old is not null) await SafeDisposeAsync(old);
    }

    public void Shutdown()
    {
        callCts?.Cancel();
        if (client is not null) _ = SafeDisposeAsync(client);
    }

    private static async Task SafeDisposeAsync(McpClient mcpClient)
    {
        try { await mcpClient.DisposeAsync(); }
        catch (Exception) { /* The server may already be gone. */ }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (client is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            items = await McpOperations.LoadItemsAsync(client, timeout.Token);
            var previous = CurrentItem;
            CurrentItem = null;
            UpdateServerItem();
            if (previous is not null && items.All.FirstOrDefault(i => i.Id == previous.Id) is { } reloaded)
                SelectItem(reloaded);
            else
                UpdateEmptyState();
            RefreshList();
        }
        catch (Exception ex)
        {
            StatusText = "Couldn't reload";
            await dialogs.InfoAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task EditSecretsAsync()
    {
        string target = (Url ?? "").Trim();
        if (target.Length == 0) return;
        bool stdio = ServerTarget.Parse(target)?.IsStdio == true;
        var values = await dialogs.EditSecretsAsync(target, stdio, HeaderStore.Get(target));
        if (values is null) return;
        HeaderStore.Set(target, values);
        UpdateSecretsButton();

        // Headers and environment go into the transport, so a live connection is made again.
        if (client is not null && string.Equals(connectedUrl, target, StringComparison.OrdinalIgnoreCase))
        {
            await DisconnectAsync();
            await ToggleConnectionAsync();
        }
        else if (client is null && State == ConnectionState.Failed)
        {
            await ToggleConnectionAsync();
        }
    }

    private void UpdateSecretsButton()
    {
        string target = (Url ?? "").Trim();
        int count = HeaderStore.Get(target).Count;
        string label = ServerTarget.Parse(target)?.IsStdio == true ? "Environment" : "Headers";
        SecretsButtonText = count == 0 ? label : $"{label} ({count})";
    }

    private void UpdateState(ConnectionState newState)
    {
        State = newState;
        StatusText = newState switch
        {
            ConnectionState.Connecting => "Connecting…",
            ConnectionState.Connected when client is not null => $"Connected to {McpOperations.ServerName(client)}",
            ConnectionState.Failed => "Connection failed",
            _ => "Not connected",
        };
        ConnectButtonText = newState switch
        {
            ConnectionState.Connected => "Disconnect",
            ConnectionState.Connecting => "Connecting…",
            _ => "Connect",
        };
        NotifyItemView();
        UpdateEmptyState();
    }

    private void UpdateServerItem()
    {
        if (client is null) return;
        var parts = new List<string> { $"Version {client.ServerInfo.Version}", Plural(items.Tools.Count, "tool") };
        if (items.Resources.Count > 0) parts.Add(Plural(items.Resources.Count, "resource"));
        if (items.Prompts.Count > 0) parts.Add(Plural(items.Prompts.Count, "prompt"));
        ServerTitle = McpOperations.ServerName(client);
        ServerSubtitle = string.Join(" · ", parts);
    }

    private void UpdateEmptyState()
    {
        OverviewChips.Clear();
        EmptyIsError = false;
        switch (State)
        {
            case ConnectionState.Connected when client is not null:
                EmptyGlyph = "globe";
                EmptyHeading = McpOperations.ServerName(client);
                EmptyBody = (string.IsNullOrWhiteSpace(client.ServerInstructions) ? "This server didn't send any instructions." : client.ServerInstructions.Trim())
                            + "\n\nPick something on the left to try it.";
                OverviewChips.Add(new Chip($"Version {client.ServerInfo.Version}"));
                if (!string.IsNullOrEmpty(client.NegotiatedProtocolVersion)) OverviewChips.Add(new Chip($"Protocol {client.NegotiatedProtocolVersion}"));
                OverviewChips.Add(new Chip(Plural(items.Tools.Count, "tool")));
                if (client.ServerCapabilities?.Resources is not null) OverviewChips.Add(new Chip(Plural(items.Resources.Count, "resource")));
                if (client.ServerCapabilities?.Prompts is not null) OverviewChips.Add(new Chip(Plural(items.Prompts.Count, "prompt")));
                break;
            case ConnectionState.Connecting:
                EmptyGlyph = "plug";
                EmptyHeading = "Connecting…";
                EmptyBody = (Url ?? "").Trim();
                break;
            case ConnectionState.Failed:
                EmptyGlyph = "error";
                EmptyIsError = true;
                EmptyHeading = "Couldn't connect";
                EmptyBody = (ConnectError ?? "") + (ServerTarget.Parse(Url ?? "")?.IsStdio == true
                    ? "\n\nCheck the command, and that it starts an MCP server that talks over stdin/stdout."
                    : "\n\nCheck that the server is running and that the URL is right.");
                break;
            default:
                EmptyGlyph = "plug";
                EmptyHeading = "Connect to an MCP server";
                EmptyBody = "Enter the server's Streamable HTTP address above, or the command that starts a stdio server (for example npx -y @modelcontextprotocol/server-everything), and press Connect. Its tools, resources and prompts appear on the left, and you can call any of them with your own arguments.";
                break;
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    // ------------------------------------------------------------ sidebar

    private void RefreshList()
    {
        int tab = SidebarTab;
        string query = (SearchText ?? "").Trim();
        bool Matches(string? text) => text?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
        IsHistoryTab = tab == HistoryTab;

        suppressSelection = true;
        Entries.Clear();
        Entry? selected = null;
        if (tab == HistoryTab)
        {
            var all = client is null ? [] : history.ForServer(connectedUrl).ToList();
            foreach (var past in all.Where(h => query.Length == 0 || Matches(h.Name) || Matches(h.Arguments)))
                Entries.Add(HistoryRow(past));
            SectionTitle = "HISTORY";
            SectionCount = all.Count == 0 ? "" : query.Length == 0 ? $"{all.Count:N0}" : $"{Entries.Count:N0} of {all.Count:N0}";
            EmptyListText = client is null ? "Connect to a server to see its history."
                : all.Count == 0 ? "Calls you make show up here. Double-click one to run it again."
                : "Nothing matches your search.";
        }
        else
        {
            var (list, title, noun, offered) = tab switch
            {
                ResourcesTab => (items.Resources, "RESOURCES", "resources", client?.ServerCapabilities?.Resources is not null),
                PromptsTab => (items.Prompts, "PROMPTS", "prompts", client?.ServerCapabilities?.Prompts is not null),
                _ => (items.Tools, "TOOLS", "tools", client?.ServerCapabilities?.Tools is not null),
            };
            foreach (var item in list.Where(i => query.Length == 0 || Matches(i.Name) || Matches(i.Title) || Matches(i.Description) || Matches(i.Key)))
            {
                var entry = new Entry(item.Title ?? item.Name, item.Subtitle, item);
                Entries.Add(entry);
                if (ReferenceEquals(item, CurrentItem)) selected = entry;
            }
            SectionTitle = title;
            SectionCount = list.Count == 0 ? "" : query.Length == 0 ? $"{list.Count:N0}" : $"{Entries.Count:N0} of {list.Count:N0}";
            EmptyListText = client is null ? $"Connect to a server to see its {noun}."
                : !offered ? $"This server doesn't offer {noun}."
                : list.Count == 0 ? $"This server has no {noun}."
                : "Nothing matches your search.";
        }
        SelectedEntry = selected;
        suppressSelection = false;
        OnPropertyChanged(nameof(ListIsEmpty));
    }

    public bool ListIsEmpty => Entries.Count == 0;

    private static Entry HistoryRow(HistoryEntry entry)
    {
        string arguments = "No arguments";
        try
        {
            if (JsonNode.Parse(entry.Arguments) is JsonObject { Count: > 0 } args)
                arguments = string.Join(", ", args.Select(kv => $"{kv.Key}: {kv.Value?.ToJsonString() ?? "null"}"));
        }
        catch (System.Text.Json.JsonException)
        {
            arguments = entry.Arguments;
        }
        var local = entry.Time.ToLocalTime();
        string time = local.Date == DateTime.Today ? local.ToString("t") : local.ToString("g");
        return new Entry(entry.Name, arguments, entry, time, entry.Status);
    }

    [RelayCommand]
    private void ShowOverview()
    {
        if (client is null) return;
        CurrentItem = null;
        if (SidebarTab != HistoryTab)
        {
            suppressSelection = true;
            SelectedEntry = null;
            suppressSelection = false;
        }
        UpdateEmptyState();
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (client is null) return;
        if (!await dialogs.ConfirmAsync("Clear the call history for this server?")) return;
        history.Clear(connectedUrl);
        RefreshList();
    }

    /// <summary>Double-click on a history row: bring it back and run it.</summary>
    public async Task RunHistoryEntryAsync()
    {
        if (SidebarTab == HistoryTab && SelectedEntry?.Tag is HistoryEntry)
            await RunAsync();
    }

    // ------------------------------------------------------------ selecting

    private void SelectItem(McpItem item)
    {
        CurrentItem = item;
        ItemTitle = item.Title ?? item.Name;
        string kindName = item.Kind switch
        {
            ItemKind.Tool => "tool",
            ItemKind.Prompt => "prompt",
            ItemKind.ResourceTemplate => "resource template",
            _ => "resource",
        };
        ItemDescription = item.Description ?? $"This {kindName} has no description.";
        RunText = item.ActionText;
        ResultTitle = item.ResultTitle;

        ItemChips.Clear();
        switch (item.Source)
        {
            case McpClientTool tool:
                if (ItemTitle != tool.Name) ItemChips.Add(new Chip(tool.Name));
                if (tool.ProtocolTool.Annotations is { } a)
                {
                    if (a.ReadOnlyHint == true) ItemChips.Add(new Chip("✓ Read-only", "success"));
                    if (a.DestructiveHint == true && a.ReadOnlyHint != true) ItemChips.Add(new Chip("⚠ Destructive", "danger"));
                    if (a.IdempotentHint == true) ItemChips.Add(new Chip("Idempotent"));
                    if (a.OpenWorldHint == true) ItemChips.Add(new Chip("Open world"));
                }
                break;
            case McpClientResource resource:
                ItemChips.Add(new Chip(resource.Uri));
                if (!string.IsNullOrEmpty(resource.MimeType)) ItemChips.Add(new Chip(resource.MimeType));
                break;
            case McpClientResourceTemplate template:
                ItemChips.Add(new Chip(template.UriTemplate));
                if (!string.IsNullOrEmpty(template.MimeType)) ItemChips.Add(new Chip(template.MimeType));
                break;
            case McpClientPrompt:
                ItemChips.Add(new Chip("Prompt", "accent"));
                break;
        }

        Fields.Clear();
        foreach (var field in ArgumentField.FromSchema(item.Schema))
            Fields.Add(new FieldViewModel(field));
        HasArguments = Fields.Count > 0;
        if (item.Kind != ItemKind.Resource)
        {
            int required = Fields.Count(f => f.Required);
            string args = Fields.Count == 0 ? "No arguments" : Fields.Count == 1 ? "1 argument" : $"{Fields.Count} arguments";
            if (required > 0) args += $" · {required} required";
            ItemChips.Add(new Chip(args));
        }
        ShowArgumentsCard = item.Kind != ItemKind.Resource;
        SchemaJson = JsonText.Format(item.Schema);
        RawJson = Pretty(McpOperations.RawTemplate(item.Schema));
        LoadPresetNames();
        ShowResult();
        NotifyItemView();
    }

    private async Task OpenHistoryAsync(HistoryEntry past)
    {
        var item = items.All.FirstOrDefault(i => i.Kind == past.Kind && i.Key == past.Key);
        if (item is null)
        {
            await dialogs.InfoAsync($"{past.Name} is no longer offered by this server.");
            return;
        }
        if (!ReferenceEquals(item, CurrentItem))
            SelectItem(item);
        ApplyArguments(past.Arguments);
        results[item.Id] = new StoredResult(past.Status, $"{past.Meta} · {past.Time.ToLocalTime():g}", past.Text);
        ShowResult();
    }

    private void ApplyArguments(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject args) return;
        RawJson = Pretty(args);
        foreach (var field in Fields)
            field.Show(args.TryGetPropertyValue(field.Name, out var value) ? value?.DeepClone() : null);
        bool fits = args.All(kv => Fields.Any(f => f.Name == kv.Key));
        if (!fits) ArgumentsPage = RawPage;
        else if (ArgumentsPage == RawPage) ArgumentsPage = FormPage;
    }

    private static string Pretty(JsonNode node) => node.ToJsonString(JsonText.Pretty).ReplaceLineEndings(Environment.NewLine);

    // ------------------------------------------------------------ arguments

    private bool TryParseRaw(out JsonObject arguments)
    {
        bool ok = McpOperations.TryParseArguments(RawJson, out arguments, out var message);
        RawStatus = message;
        RawOk = ok;
        return ok;
    }

    private void ValidateRaw() => TryParseRaw(out _);

    [RelayCommand]
    private void FormatRaw()
    {
        if (!string.IsNullOrWhiteSpace(RawJson) && TryParseRaw(out var parsed))
            RawJson = Pretty(parsed);
    }

    [RelayCommand]
    private void CopyFromForm() => RawJson = Pretty(FilledArguments());

    private JsonObject FilledArguments()
    {
        var arguments = new JsonObject();
        foreach (var field in Fields)
            if (field.TryRead(showErrors: false, out var value, out var present) && present)
                arguments[field.Name] = value;
        return arguments;
    }

    private bool TryGetFormArguments(out JsonObject arguments)
    {
        arguments = [];
        bool ok = true;
        foreach (var field in Fields)
        {
            if (field.TryRead(showErrors: true, out var value, out var present))
            {
                if (present) arguments[field.Name] = value;
            }
            else
            {
                ok = false;
            }
        }
        return ok;
    }

    // ------------------------------------------------------------ saved values

    private string PresetKey(McpItem item) => AppSettings.PresetKey(connectedUrl, item);

    private void LoadPresetNames()
    {
        PresetNames.Clear();
        if (CurrentItem is not null && settings.Presets.TryGetValue(PresetKey(CurrentItem), out var saved))
            foreach (var name in saved.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                PresetNames.Add(name);
        OnPropertyChanged(nameof(HasPresets));
    }

    public bool HasPresets => PresetNames.Count > 0;

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        var item = CurrentItem;
        if (item is null) return;
        JsonObject args;
        if (ArgumentsPage == RawPage)
        {
            if (!TryParseRaw(out args)) return;
        }
        else
        {
            args = FilledArguments();
        }
        string? name = await dialogs.AskNameAsync("Save values", $"Name for these {item.Name} values:");
        if (name is null) return;
        string key = PresetKey(item);
        if (!settings.Presets.TryGetValue(key, out var saved))
            settings.Presets[key] = saved = [];
        if (saved.ContainsKey(name) && !await dialogs.ConfirmAsync($"Replace the saved values \"{name}\"?"))
            return;
        saved[name] = args.ToJsonString();
        settings.Save();
        LoadPresetNames();
    }

    [RelayCommand]
    private void LoadPreset(string name)
    {
        if (CurrentItem is not null && settings.Presets.TryGetValue(PresetKey(CurrentItem), out var saved) && saved.TryGetValue(name, out var json))
            ApplyArguments(json);
    }

    [RelayCommand]
    private void DeletePreset(string name)
    {
        if (CurrentItem is null) return;
        string key = PresetKey(CurrentItem);
        if (!settings.Presets.TryGetValue(key, out var saved)) return;
        saved.Remove(name);
        if (saved.Count == 0) settings.Presets.Remove(key);
        settings.Save();
        LoadPresetNames();
    }

    // ------------------------------------------------------------ calling

    [RelayCommand]
    private async Task RunAsync()
    {
        var item = CurrentItem;
        var mcpClient = client;
        if (item is null || mcpClient is null || callCts is not null) return;

        JsonObject arguments = [];
        if (item.Kind != ItemKind.Resource)
        {
            if (ArgumentsPage == RawPage)
            {
                if (!TryParseRaw(out arguments)) return;
            }
            else if (!TryGetFormArguments(out arguments))
            {
                ArgumentsPage = FormPage;
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
        NotifyItemView();

        StoredResult outcome;
        try
        {
            var result = await McpOperations.RunAsync(mcpClient, item, arguments, cts.Token);
            callWatch.Stop();
            outcome = new StoredResult(result.Status, Meta(result.Text), result.Text);
        }
        catch (OperationCanceledException)
        {
            callWatch.Stop();
            outcome = new StoredResult(ResultStatus.Cancelled, Meta(null), "The call was cancelled before the server answered.");
        }
        catch (Exception ex)
        {
            callWatch.Stop();
            outcome = new StoredResult(ResultStatus.Failed, Meta(null), ex.Message);
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
            if (SidebarTab == HistoryTab) RefreshList();
        }
        if (CurrentItem?.Id == item.Id) ShowResult();
        NotifyItemView();
    }

    [RelayCommand]
    private void Cancel() => callCts?.Cancel();

    [RelayCommand]
    private async Task CopyResultAsync()
    {
        if (ResultText.Length > 0) await dialogs.CopyAsync(ResultText);
    }

    private string Elapsed()
    {
        double seconds = callWatch.Elapsed.TotalSeconds;
        return seconds < 10 ? $"{seconds:0.00} s" : $"{seconds:0.0} s";
    }

    private string Meta(string? text) =>
        text is null ? Elapsed() : $"{Elapsed()} · {Plural(text.Count(c => c == '\n') + 1, "line")}";

    private void ShowResult()
    {
        var item = CurrentItem;
        bool running = item is not null && runningId == item.Id;
        IsRunning = running;
        if (running)
        {
            ResultStatus = ResultStatus.Running;
            ResultMeta = Elapsed();
            ResultText = "";
            ResultPlaceholder = "Waiting for the server…";
        }
        else if (item is not null && results.TryGetValue(item.Id, out var result))
        {
            ResultStatus = result.Status;
            ResultMeta = result.Meta;
            ResultText = result.Text;
        }
        else
        {
            ResultStatus = ResultStatus.None;
            ResultMeta = "";
            ResultText = "";
            ResultPlaceholder = item?.Kind switch
            {
                ItemKind.Resource or ItemKind.ResourceTemplate => "Press Read to see the resource's contents.",
                ItemKind.Prompt => "Press Get to see the prompt's messages.",
                _ => "Run the tool to see its result here.",
            };
        }
    }
}
