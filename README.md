# User Context Retrieval MCP Server

![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-Apache%202.0-blue)

A [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that gives AI agents the ability to ask users clarifying questions mid-task via a pop-up terminal window. Built with .NET 8 and C#.

## The Problem

Long-running AI agents (like [Claude Code](https://docs.anthropic.com/en/docs/claude-code), IDE copilots, or custom agentic workflows) often encounter situations where they need human input — ambiguous requirements, unexpected errors, or design decisions that could go multiple ways. Without a mechanism to ask, the agent either guesses (often incorrectly) or stops entirely and waits for the user to notice.

## The Solution

This MCP server exposes a single tool, `user_context_retrieval`, that an AI agent can call at any point during execution. When invoked, a **new terminal window** opens on the user's machine displaying the agent's questions. The user types their answers and the responses are returned directly to the agent, allowing it to continue working with the additional context.

![Terminal prompt demo](./demo.png)

## Features

- **Pop-up terminal prompt** — questions appear in a dedicated window, separate from the agent's own I/O
- **Multi-question support** — the agent can ask multiple questions in a single call; all are displayed upfront as a numbered list
- **Structured responses** — answers are returned as clearly formatted Q&A pairs the agent can parse
- **Timeout handling** — 10-minute response window with graceful fallback messaging
- **Window-closed detection** — if the user closes the prompt without answering, the agent receives a clear notification
- **Stdio transport** — communicates over stdin/stdout per the MCP specification, compatible with any MCP client

## Prerequisites

- **Windows**: PowerShell 5.1+ (included with Windows 10/11)
- **macOS/Linux** *(untested)*: [PowerShell Core (`pwsh`)](https://github.com/PowerShell/PowerShell#get-powershell) must be installed

> **Note:** If you use a [pre-built release](#option-1-download-a-pre-built-release-recommended), no other dependencies are required. Building from source requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

## Installation

### Option 1: Download a Pre-built Release (Recommended)

Self-contained executables are available on the [Releases](https://github.com/Jacob-J-Thomas/user-context-retrieval-mcp-server/releases) page. No .NET SDK required.

1. Download the zip for your platform from the [latest release](https://github.com/Jacob-J-Thomas/user-context-retrieval-mcp-server/releases/latest):

   | Platform             | Asset |
   |----------------------|-------|
   | Windows (x64)        | `UserContextRetrievalMcpServer-win-x64.zip` |
   | macOS (Intel)        | `UserContextRetrievalMcpServer-osx-x64.zip` |
   | macOS (Apple Silicon) | `UserContextRetrievalMcpServer-osx-arm64.zip` |
   | Linux (x64)          | `UserContextRetrievalMcpServer-linux-x64.zip` |

2. Extract the zip to a permanent location, for example:

   - **Windows**: `C:\Tools\UserContextRetrievalMcpServer\`
   - **macOS / Linux**: `~/tools/UserContextRetrievalMcpServer/`

3. Continue to the [Configuration](#configuration) section below to register the server with your MCP client.

### Option 2: Build from Source

Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

```bash
git clone https://github.com/Jacob-J-Thomas/user-context-retrieval-mcp-server.git
cd user-context-retrieval-mcp-server
dotnet publish UserContextRetrievalMcpServer -c Release -r win-x64 -o ./publish
```

> Replace `win-x64` with your platform's [runtime identifier](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) (e.g. `osx-arm64`, `linux-x64`).

## Configuration

This server uses the **stdio** transport — it does not listen on a port. The MCP client (e.g., Claude Code) launches the server process and communicates with it over stdin/stdout.

In every example below, replace the path with the actual location of your extracted or published executable.

### Claude Code (CLI)

```bash
claude mcp add user-context-retrieval -- /path/to/UserContextRetrievalMcpServer.exe
```

### Claude Desktop

Add the following to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "user-context-retrieval": {
      "command": "/path/to/UserContextRetrievalMcpServer.exe"
    }
  }
}
```

**Config file location:**

| Platform | Path |
|----------|------|
| Windows  | `%APPDATA%\Claude\claude_desktop_config.json` |
| macOS    | `~/Library/Application Support/Claude/claude_desktop_config.json` |

### Cursor

Add the following to `.cursor/mcp.json` (project-level) or `~/.cursor/mcp.json` (global):

```json
{
  "mcpServers": {
    "user-context-retrieval": {
      "command": "/path/to/UserContextRetrievalMcpServer.exe"
    }
  }
}
```

### OpenAI Codex CLI

Add the following to `~/.codex/config.toml` (global) or `.codex/config.toml` (project-level):

```toml
[mcp_servers.user-context-retrieval]
command = "/path/to/UserContextRetrievalMcpServer.exe"
```

Or via the CLI:

```bash
codex mcp add user-context-retrieval -- /path/to/UserContextRetrievalMcpServer.exe
```

### Other MCP Clients

Any MCP client that supports the stdio transport can launch this server. Point it at the executable.

## Usage

Once configured, the `user_context_retrieval` tool is available to the AI agent automatically. The agent decides when to invoke it based on its own judgment — no manual triggering is required.

### Tool Reference

**Tool name:** `user_context_retrieval`

| Parameter   | Type       | Required | Description |
|-------------|------------|----------|-------------|
| `reason`    | `string`   | Yes      | A clear explanation of why the agent needs user input. Displayed prominently in the terminal window. |
| `questions` | `string[]` | Yes      | A list of specific questions. Displayed as a numbered list; the user answers each in sequence. |

### Example Tool Call

```json
{
  "name": "user_context_retrieval",
  "arguments": {
    "reason": "The project has no database configuration and I need to set one up.",
    "questions": [
      "Which database engine should I use (PostgreSQL, SQLite, SQL Server)?",
      "Should I include Entity Framework Core as the ORM?"
    ]
  }
}
```

### Example Response

```
User responded to 2 question(s):

1. Q: Which database engine should I use (PostgreSQL, SQLite, SQL Server)?
   A: PostgreSQL

2. Q: Should I include Entity Framework Core as the ORM?
   A: Yes, use EF Core with code-first migrations
```

### Terminal Window Behavior

When the tool is invoked, a new PowerShell window opens displaying:

1. A header indicating an AI agent needs input
2. The agent's stated **reason** for asking
3. All **questions** as a numbered list
4. Numbered input prompts (`1>`, `2>`, etc.) — one Enter per answer
5. A confirmation message before the window auto-closes

If the user does not respond within **10 minutes**, or closes the window without answering, the agent receives a descriptive fallback message and can decide how to proceed.

## Architecture

```
UserContextRetrievalMcpServer/
├── UserContextRetrievalMcpServer.csproj   # Project file (.NET 8)
├── Program.cs                              # MCP server entry point (stdio transport)
└── Tools/
    └── UserContextRetrievalTool.cs         # Tool implementation
```

### How It Works Internally

1. The MCP client sends a `tools/call` JSON-RPC request over stdin
2. The server writes the questions to a temporary JSON file
3. A PowerShell script is generated and launched in a **new terminal window**
4. The user sees the questions and types answers sequentially
5. Answers are written to a response JSON file; the terminal closes
6. The server reads the response file, formats it, cleans up temp files, and returns the result over stdout

All temporary files are created under `%TEMP%/UserContextRetrievalMcpServer/<session-guid>/` and are cleaned up after each invocation regardless of outcome.

## Contributing

Contributions are welcome! To make sure your change gets merged, please **open an issue first** to discuss the proposed change and get approval before starting work.

1. **Fork** the repository
2. **Create a feature branch** (`git checkout -b feature/my-feature`)
3. **Make your changes** and ensure the project builds cleanly (`dotnet build`)
4. **Commit** with a clear, descriptive message
5. **Open a pull request** against `develop`

### Development Setup

```bash
git clone https://github.com/Jacob-J-Thomas/user-context-retrieval-mcp-server.git
cd user-context-retrieval-mcp-server
dotnet restore UserContextRetrievalMcpServer/UserContextRetrievalMcpServer.csproj
dotnet build UserContextRetrievalMcpServer/UserContextRetrievalMcpServer.csproj
```

### Areas for Contribution

- **Cross-platform support** — the macOS and Linux terminal launching is currently stubbed out and untested
- **Test coverage** — no test project exists yet
- **Additional MCP tools** — if the project scope expands beyond a single tool

## License

This project is licensed under the **[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)**. See [LICENSE](LICENSE) for details.

## Author

**Jacob Thomas** — [@Jacob-J-Thomas](https://github.com/Jacob-J-Thomas)
