using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using McpClientUI.Desktop.ViewModels;
using McpClientUI.Desktop.Views;

namespace McpClientUI.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Follows the system light/dark setting; "--theme light" or "--theme dark" overrides it.
            string? theme = desktop.Args?.SkipWhile(a => a != "--theme").Skip(1).FirstOrDefault()?.ToLowerInvariant();
            RequestedThemeVariant = theme switch { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => ThemeVariant.Default };

            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
            var window = new MainWindow { Title = version is null or "1.0.0" ? "MCP Client" : $"MCP Client {version}" };
            var viewModel = new MainViewModel(new Dialogs(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => viewModel.Shutdown();

            // A failure inside a command (a dialog, the clipboard) should not close the app.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                System.Diagnostics.Trace.TraceError(e.Exception.ToString());
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
