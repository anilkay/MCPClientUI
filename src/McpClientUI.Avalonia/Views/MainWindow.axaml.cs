using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using McpClientUI.Desktop.ViewModels;

namespace McpClientUI.Desktop.Views;

public partial class MainWindow : Window
{
    private MainViewModel? viewModel;

    public MainWindow()
    {
        InitializeComponent();
        UrlBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || viewModel is null) return;
            e.Handled = true;
            if (!viewModel.IsConnected) await viewModel.ToggleConnectionCommand.ExecuteAsync(null);
        };
        // Enter in a single-line argument box runs the call.
        FormFields.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || viewModel is null) return;
            if (e.Source is TextBox { AcceptsReturn: false })
            {
                e.Handled = true;
                await viewModel.RunCommand.ExecuteAsync(null);
            }
        }, RoutingStrategies.Tunnel);
        EntryList.DoubleTapped += async (_, _) =>
        {
            if (viewModel is not null) await viewModel.RunHistoryEntryAsync();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (command && e.Key == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => RenderAll();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel = DataContext as MainViewModel;
        if (viewModel is not null) viewModel.PropertyChanged += OnViewModelChanged;
        RenderAll();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ResultText) or nameof(MainViewModel.ResultStatus) or nameof(MainViewModel.WrapResult):
                RenderResult();
                break;
            case nameof(MainViewModel.SchemaJson):
                RenderJson(SchemaBlock, viewModel!.SchemaJson);
                break;
            case nameof(MainViewModel.ShowArgumentsCard):
                UpdateRows();
                break;
        }
    }

    private void RenderAll()
    {
        RenderResult();
        if (viewModel is not null) RenderJson(SchemaBlock, viewModel.SchemaJson);
        UpdateRows();
    }

    /// <summary>A plain resource has no arguments: the result takes the whole height.</summary>
    private void UpdateRows()
    {
        bool args = viewModel?.ShowArgumentsCard ?? true;
        WorkGrid.RowDefinitions[0].Height = args ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        WorkGrid.RowDefinitions[1].Height = args ? new GridLength(12) : new GridLength(0);
    }

    private void RenderResult()
    {
        if (viewModel is null) return;
        bool wrap = viewModel.WrapResult;
        ResultBlock.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ResultScroll.HorizontalScrollBarVisibility = wrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        string text = viewModel.ResultText;
        if (viewModel.ResultIsFailure)
            RenderPlain(ResultBlock, text, Brush("DangerBrush"));
        else if (viewModel.ResultStatus == ResultStatus.Cancelled)
            RenderPlain(ResultBlock, text, Brush("SecondaryTextBrush"));
        else
            RenderJson(ResultBlock, text);
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private static void RenderPlain(SelectableTextBlock block, string text, IBrush? color)
    {
        block.Inlines = null;
        block.Text = text;
        block.Foreground = color;
    }

    /// <summary>Colors JSON (keys, strings, numbers, literals); anything else is shown as plain text.</summary>
    private void RenderJson(SelectableTextBlock block, string text)
    {
        block.Foreground = Brush("TextBrush");
        if (!JsonText.TryFormat(text, out var json))
        {
            RenderPlain(block, text, Brush("TextBrush"));
            return;
        }
        IBrush? key = Brush("JsonKeyBrush"), str = Brush("JsonStringBrush"), number = Brush("JsonNumberBrush"),
            literal = Brush("JsonLiteralBrush"), punctuation = Brush("JsonPunctuationBrush");
        var inlines = new InlineCollection();
        int i = 0;
        while (i < json.Length)
        {
            char c = json[i];
            int start = i;
            IBrush? color = null;
            if (c == '"')
            {
                i++;
                while (i < json.Length && json[i] != '"') i += json[i] == '\\' ? 2 : 1;
                i = Math.Min(i + 1, json.Length);
                int next = i;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                color = next < json.Length && json[next] == ':' ? key : str;
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                while (i < json.Length && (char.IsAsciiDigit(json[i]) || json[i] is '-' or '+' or '.' or 'e' or 'E')) i++;
                color = number;
            }
            else if (char.IsAsciiLetter(c))
            {
                while (i < json.Length && char.IsAsciiLetter(json[i])) i++;
                color = literal;
            }
            else if (c is '{' or '}' or '[' or ']' or ',' or ':')
            {
                i++;
                color = punctuation;
            }
            else
            {
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i == start) i++;
            }
            var run = new Run(json[start..i]);
            if (color is not null) run.Foreground = color;
            inlines.Add(run);
        }
        block.Text = null;
        block.Inlines = inlines;
    }
}
