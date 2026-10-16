<img src="docs/logo.png" alt="MCP Client logo" width="96" align="right">

# MCP Client

A small Windows Forms app for trying out MCP servers that speak the Streamable HTTP transport.
Built on the official [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) C# SDK.

![Calling a tool (light theme)](docs/light-tool.png)

```powershell
dotnet run --project McpClientUI.csproj               # follows the Windows light/dark setting
dotnet run --project McpClientUI.csproj -- --theme dark
```

- Enter the server URL (default `http://localhost:5199/mcp`) and press **Connect**. The last URL and recent URLs are remembered.
- **Headers** sets a Bearer token and any other HTTP headers (API keys, ...) for that URL. They are stored encrypted
  for your Windows account (DPAPI) in `%AppData%\McpClientUI\headers.dat`.
- The sidebar has four tabs, all searchable:
  - **Tools**: pick one to see its description, hints (read-only, open world, ...) and a form built from its input schema:
    text and number boxes, true/false switches, JSON boxes for objects and arrays, defaults filled in, required fields checked.
  - **Resources**: plain resources are read with **Read**; resource templates such as `users/{id}` get a form for their variables.
  - **Prompts**: fill in the prompt's arguments and press **Get** to see its messages.
  - **History**: every call with its arguments, status, time and result, kept between runs. Click one to bring back its
    arguments and result, double-click to run it again.
- Click the server entry for its version, protocol and instructions.
- **Raw JSON** sends hand-written arguments instead of the form; **Schema** shows the input schema.
- **Saved** stores the current argument values under a name (per server and tool) and loads them back later.
- **Run** calls the tool; the result shows its status and time, JSON results are colored. Calls can be cancelled.

Shortcuts: `Ctrl+Enter` run, `Enter` in a field run, `Esc` cancel, `Ctrl+F` search, `F5` reload from the server.

## Screenshots

| Dark theme | Server overview |
| --- | --- |
| ![Dark theme](docs/dark-tool.png) | ![Server overview](docs/dark-overview.png) |

| Prompts | Resource templates |
| --- | --- |
| ![Getting a prompt](docs/prompt.png) | ![Reading a resource template](docs/resource-template.png) |

| History | Request headers |
| --- | --- |
| ![Call history](docs/dark-history.png) | ![Bearer token and headers](docs/headers-dialog.png) |

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
`McpClientUI.exe` (no .NET install needed, ~47 MB) and attaches it as a zip to the
[GitHub release](https://github.com/anilkay/MCPClientUI/releases).

## Build options

| Build | Size | Needs | Command |
| --- | --- | --- | --- |
| Self-contained single file (what releases ship) | ~47 MB exe | nothing | `dotnet publish McpClientUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish` |
| Framework-dependent single file | ~2.4 MB exe (~0.8 MB zipped) | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) | `dotnet publish McpClientUI.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish` |

The self-contained build can't get much smaller: Windows Forms doesn't support trimming, and the
single file is already compressed.
