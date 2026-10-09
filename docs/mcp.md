# Using vvo as an MCP server

`vvo mcp` runs an [MCP](https://modelcontextprotocol.io) server over stdio, so an agent can work with
`.vvo` catalogues through tools instead of running `vvo` commands itself.

## Tools

Every command becomes a tool named after it:

| Tools | Do |
|---|---|
| `db_new`, `db_info`, `db_copy`, `db_shrink`, `db_export`, `db_import` | Create, describe and look after catalogues |
| `volume_list`, `volume_create`, `volume_update`, `volume_delete` | Virtual volumes |
| `folder_list`, `folder_scan`, `folder_update`, `folder_copy`, `folder_move`, `folder_delete`, `folder_rescan` | Folders in a volume |
| `ls`, `tree`, `stat`, `search` | Look inside the catalogued trees |
| `compare` | Diff two folders, or a folder against the disk |
| `add`, `rm` | Add files from disk under a folder, or remove records |
| `about` | Version, license and third-party notices |

- A command's arguments and options become the tool's parameters, so `--include-unchanged` is
  `include_unchanged`. Each tool's description and parameters come from `vvo <command> --help`.
- A tool answers with the same JSON the command prints. A failure is an error result holding
  `{ "error": { "code", "message", ... } }`, with the codes listed in the [README](../README.md#command-line).
- Progress, such as a scan's, is sent as MCP progress notifications to clients that ask for it.

## The catalogue

Every tool that works on a catalogue takes `db`, the path of the `.vvo` file. Start the server with
`vvo mcp --db <path>` to make one catalogue the default: `db` is then optional, and a call can still
name another.

## Confirmation

Deleting, rescanning and replacing a file still need you to type the word (`delete`, `update` or
`replace`), as at a terminal:

- Clients that support MCP elicitation ask you in a form. The form is shown to you, not to the agent,
  so the agent cannot answer it.
- Other clients get `confirmation_required`, with the warning and the exact `command` to run yourself
  in a terminal. Nothing is changed.

`compare` with `disk` shows what a rescan would change without asking anything.

## Stopping

The server ends when the client disconnects or closes it. `vvo mcp stop` stops every `vvo mcp` server
you are running, and prints the ones it stopped:

```json
{ "stopped": [ { "pid": 12345, "startedAt": "2026-10-09T08:00:00Z", "db": "D:\\Catalogues\\backup.vvo" } ] }
```

## Adding it to a client

The examples use `C:\Tools\vvo.exe` and `D:\Catalogues\backup.vvo`; replace them with your own paths.
In JSON, backslashes are written twice. Leave out `"--db", "..."` to name the catalogue on each call
instead.

### Claude Code

`.mcp.json` in the project, or run
`claude mcp add vvo -- C:\Tools\vvo.exe mcp --db D:\Catalogues\backup.vvo`:

```json
{
  "mcpServers": {
    "vvo": {
      "command": "C:\\Tools\\vvo.exe",
      "args": ["mcp", "--db", "D:\\Catalogues\\backup.vvo"]
    }
  }
}
```

### Claude Desktop

The same `mcpServers` block, in `claude_desktop_config.json` (Settings → Developer → Edit Config).

### Codex

`~/.codex/config.toml` (Codex uses TOML rather than JSON), or run
`codex mcp add vvo -- C:\Tools\vvo.exe mcp --db D:\Catalogues\backup.vvo`:

```toml
[mcp_servers.vvo]
command = 'C:\Tools\vvo.exe'
args = ["mcp", "--db", 'D:\Catalogues\backup.vvo']
```

### GitHub Copilot in VS Code

`.vscode/mcp.json` in the workspace, or **MCP: Open User Configuration** for every workspace:

```json
{
  "servers": {
    "vvo": {
      "type": "stdio",
      "command": "C:\\Tools\\vvo.exe",
      "args": ["mcp", "--db", "D:\\Catalogues\\backup.vvo"]
    }
  }
}
```

### GitHub Copilot CLI

`~/.copilot/mcp-config.json`, or `/mcp add` in a session:

```json
{
  "mcpServers": {
    "vvo": {
      "type": "local",
      "command": "C:\\Tools\\vvo.exe",
      "args": ["mcp", "--db", "D:\\Catalogues\\backup.vvo"],
      "tools": ["*"]
    }
  }
}
```

### Any other client

Start `vvo` with the arguments `mcp` (and `--db <path>` if you like) as a stdio server. Most clients
take the `mcpServers` shape shown for Claude Code.
