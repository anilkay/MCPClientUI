# MCP Client

A small Windows Forms app for trying out MCP servers that speak the Streamable HTTP transport.
Built on the official [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) C# SDK.

![Calling a tool (light theme)](docs/light-tool.png)

```powershell
dotnet run --project McpClientUI.csproj               # follows the Windows light/dark setting
dotnet run --project McpClientUI.csproj -- --theme dark
```

- Enter the server URL (default `http://localhost:5199/mcp`) and press **Connect**. The last URL and recent URLs are remembered.
- The left side lists the server's tools; click the server entry for its version, protocol and instructions.
- Pick a tool to see its description, hints (read-only, open world, ...) and a form built from its input schema:
  text and number boxes, true/false switches, JSON boxes for objects and arrays, defaults filled in, required fields checked.
- **Raw JSON** sends hand-written arguments instead; **Schema** shows the input schema.
- **Run** calls the tool; the result shows its status and time, JSON results are colored. Calls can be cancelled.

Shortcuts: `Ctrl+Enter` run, `Enter` in a field run, `Esc` cancel, `Ctrl+F` search tools, `F5` reload tools.

## Screenshots

| Dark theme | Server overview |
| --- | --- |
| ![Dark theme](docs/dark-tool.png) | ![Server overview](docs/dark-overview.png) |

When the server can't be reached, the reason is shown in place:

![Connection failed](docs/connection-failed.png)

## Requirements

- Windows 10/11 and the .NET 10 SDK.
- An MCP server with the Streamable HTTP transport, for example the one WindowsUtils starts from its AI Chat screen.

## Releases

Every push to `main` is built by `.github/workflows/build.yml`. To publish a release, run

```powershell
.\release.ps1 0.1.0
```

It pushes a `v0.1.0` tag; `.github/workflows/release.yml` then builds a self-contained single-file
`McpClientUI.exe` (no .NET install needed) and attaches it as a zip to the
[GitHub release](https://github.com/anilkay/MCPClientUI/releases).
