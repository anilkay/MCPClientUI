namespace McpClientUI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Follows the Windows light/dark setting; "--theme light" or "--theme dark" overrides it.
        string? theme = args.SkipWhile(a => a != "--theme").Skip(1).FirstOrDefault()?.ToLowerInvariant();
        Application.SetColorMode(theme switch
        {
            "dark" => SystemColorMode.Dark,
            "light" => SystemColorMode.Classic,
            _ => SystemColorMode.System,
        });
        ApplicationConfiguration.Initialize();
        Theme.Initialize();
        Application.Run(new MainForm());
    }
}
