using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace McpClientUI.Desktop.ViewModels;

public enum ConnectionState { Disconnected, Connecting, Connected, Failed }

/// <summary>Dialogs the view model needs; Views/Dialogs.cs draws them.</summary>
public interface IDialogs
{
    Task<Dictionary<string, string>?> EditSecretsAsync(string target, bool stdio, IReadOnlyDictionary<string, string> current);
    Task<string?> AskNameAsync(string title, string prompt);
    Task<bool> ConfirmAsync(string question);
    Task InfoAsync(string text);
    Task CopyAsync(string text);
}

/// <summary>A small label: "Read-only", "2 arguments · 1 required", a URI…</summary>
public sealed record Chip(string Text, string Kind = "")
{
    public bool IsSuccess => Kind == "success";
    public bool IsDanger => Kind == "danger";
    public bool IsAccent => Kind == "accent";
}

/// <summary>A row in the sidebar: a tool, resource, prompt or past call.</summary>
public sealed record Entry(string Title, string Subtitle, object Tag, string? Trailing = null, ResultStatus? Status = null)
{
    public bool HasStatus => Status is not null;
    public bool IsOk => Status == ResultStatus.Success;
    public bool IsError => Status is ResultStatus.ToolError or ResultStatus.Failed;
    public bool HasTrailing => !string.IsNullOrEmpty(Trailing);
    public override string ToString() => Title;
}

/// <summary>One argument in the form, over a shared <see cref="ArgumentField"/>.</summary>
public sealed partial class FieldViewModel : ObservableObject
{
    public FieldViewModel(ArgumentField field)
    {
        Field = field;
        text = field.DefaultText;
        choice = field.DefaultChoice;
    }

    public ArgumentField Field { get; }
    public string Name => Field.Name;
    public string TypeLabel => Field.TypeLabel;
    public bool Required => Field.Required;
    public string? Description => Field.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Field.Description);
    public bool UsesChoices => Field.UsesChoices;
    public bool UsesText => !Field.UsesChoices;
    public bool IsMultiline => Field.IsMultiline;
    public string Placeholder => Field.Placeholder;
    public IReadOnlyList<string> ChoiceLabels => Field.ChoiceLabels;

    [ObservableProperty] private string text;
    [ObservableProperty] private int choice;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string? error;

    public bool HasError => Error is not null;

    partial void OnTextChanged(string value) => Error = null;
    partial void OnChoiceChanged(int value) => Error = null;

    public bool TryRead(bool showErrors, out JsonNode? value, out bool present)
    {
        string? problem = Field.Read(Text ?? "", Choice, out value, out present);
        if (showErrors) Error = problem;
        return problem is null;
    }

    public void Show(JsonNode? value)
    {
        (Text, Choice) = (Field.Display(value).Text, Field.Display(value).Choice);
        Error = null;
    }
}
