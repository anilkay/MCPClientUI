using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using McpClientUI.Desktop.ViewModels;

namespace McpClientUI.Desktop.Views;

/// <summary>The small dialogs (headers, environment, a name, yes/no), built in code over the main window.</summary>
public sealed class Dialogs(Window owner) : IDialogs
{
    public async Task<Dictionary<string, string>?> EditSecretsAsync(string target, bool stdio, IReadOnlyDictionary<string, string> current)
    {
        var token = new TextBox { PasswordChar = '•', PlaceholderText = "Token (sent as Authorization: Bearer …)" };
        var lines = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 130,
            Classes = { "mono" },
            PlaceholderText = stdio ? "API_KEY=123" + Environment.NewLine + "LOG_LEVEL=debug" : "X-Api-Key: 123" + Environment.NewLine + "X-Team: qa",
        };
        var rows = new StackPanel { Spacing = 6 };
        rows.Children.Add(Muted(target));
        if (stdio)
        {
            lines.Text = SecretLines.FormatEnvironment(current);
            rows.Children.Add(Strong("Variables"));
            rows.Children.Add(lines);
            rows.Children.Add(Muted("One per line, as NAME=value. They are added to the server process's environment and saved encrypted for your account."));
        }
        else
        {
            (token.Text, lines.Text) = SecretLines.SplitHeaders(current);
            var show = new CheckBox { Content = "Show token" };
            show.IsCheckedChanged += (_, _) => token.PasswordChar = show.IsChecked == true ? '\0' : '•';
            rows.Children.Add(Strong("Bearer token"));
            rows.Children.Add(token);
            rows.Children.Add(show);
            rows.Children.Add(Strong("Other headers"));
            rows.Children.Add(lines);
            rows.Children.Add(Muted("One per line, as Name: value. Saved encrypted for your account and sent with every request to this URL."));
        }

        Dictionary<string, string>? result = null;
        bool ok = await ShowAsync(stdio ? "Environment variables" : "Request headers", "Save", rows, () =>
        {
            string? problem = stdio
                ? SecretLines.ParseEnvironment(lines.Text ?? "", out var variables)
                : SecretLines.ParseHeaders(token.Text ?? "", lines.Text ?? "", out variables);
            result = variables;
            return problem;
        });
        return ok ? result : null;
    }

    public async Task<string?> AskNameAsync(string title, string prompt)
    {
        var name = new TextBox();
        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        rows.Children.Add(name);
        bool ok = await ShowAsync(title, "Save", rows, () => string.IsNullOrWhiteSpace(name.Text) ? "Enter a name." : null, name);
        return ok ? name.Text!.Trim() : null;
    }

    public Task<bool> ConfirmAsync(string question) =>
        ShowAsync("MCP Client", "Yes", new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap }, () => null, cancelText: "No");

    public Task InfoAsync(string text) =>
        ShowAsync("MCP Client", "OK", new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, () => null, cancelText: null);

    public async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private IBrush? Resource(string key) =>
        owner.TryFindResource(key, owner.ActualThemeVariant, out var value) ? value as IBrush : null;

    private static TextBlock Muted(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 12 };
    private static TextBlock Strong(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 0) };

    /// <summary>A dialog with the content, an error line and OK / Cancel; <paramref name="accept"/> returns an error or null.</summary>
    private async Task<bool> ShowAsync(string title, string okText, Control content, Func<string?> accept, Control? focus = null, string? cancelText = "Cancel")
    {
        var error = new TextBlock { Classes = { "error" }, IsVisible = false };
        var okButton = new Button { Content = okText, Classes = { "accent" }, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(okButton);
        Button? cancelButton = null;
        if (cancelText is not null)
        {
            cancelButton = new Button { Content = cancelText, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
            buttons.Children.Add(cancelButton);
        }

        var body = new StackPanel { Spacing = 10, Margin = new(24, 20, 24, 16) };
        body.Children.Add(content);
        body.Children.Add(error);
        var footer = new Border { Background = Resource("WindowBrush") ?? Brushes.Transparent, Padding = new(24, 16), Child = buttons };
        var layout = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(body);

        var dialog = new Window
        {
            Title = title,
            Content = layout,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon,
            Background = Resource("SurfaceBrush"),
        };
        okButton.Click += (_, _) =>
        {
            if (accept() is { } problem)
            {
                error.Text = problem;
                error.IsVisible = true;
                return;
            }
            dialog.Close(true);
        };
        if (cancelButton is not null) cancelButton.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(false); };
        if (focus is not null) dialog.Opened += (_, _) => focus.Focus();
        return await dialog.ShowDialog<bool>(owner);
    }
}
