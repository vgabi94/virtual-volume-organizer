<p align="center">
  <img src="VVO.UI/Assets/vvo2.svg" alt="Virtual Volume Organizer" width="128" height="128">
</p>

# Virtual Volume Organizer

Virtual Volume Organizer (VVO) catalogues offline drives, backups, and USB sticks into `.vvo` database files so you can browse, search, and diff folders while the media is disconnected.

It only stores metadata (names, sizes, timestamps)—file contents are never read, hashed, or copied.

Built with C#, .NET 10, and [Avalonia UI](https://avaloniaui.net/).

## Features

- **Fast scanning** with progress, cancellation, and folder size totals.
- **Diff & compare** between two catalogued folders or against a live drive to see changes.
- **Update scans** when reconnecting media, with a diff preview before saving.
- **Virtual volumes** to group folders with custom labels, icons, and colors.
- **Undo / Redo** for folder and volume edits.
- **JSON import/export**, database snapshots, and compaction.
- **Dark and light themes**.
- **Command line** (`vvo`) for scripts and AI agents, with JSON output.

## Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+N` | New database |
| `Ctrl+O` | Open database |
| `Ctrl+Shift+S` | Save a copy |
| `Ctrl+Shift+N` | New virtual volume |
| `Ctrl+Shift+A` | Scan folder into volume |
| `Ctrl+E` | Edit folder |
| `Ctrl+D` | Duplicate folder |
| `Ctrl+F` | Search files |
| `Ctrl+Z` / `Ctrl+Y` | Undo / Redo |

## Build & Run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Run from source
dotnet run --project VVO.UI

# Run the command line from source
dotnet run --project VVO.Cli -- --help

# Run tests
dotnet test VVO.slnx
```

### Self-Contained Binary

Build a single-file executable with no runtime dependency:

```bash
dotnet publish VVO.UI/VVO.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish/win-x64
```

The command line publishes the same way:

```bash
dotnet publish VVO.Cli/VVO.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish/cli-win-x64
```

Replace `win-x64` with `linux-x64`, `osx-arm64`, or `win-arm64` as needed.

> **Note**: Do not enable `PublishTrimmed`—views are resolved via reflection. The app is portable and writes `settings.json` next to the binary.

## Command Line

`vvo.exe` is a separate download from the app. It reads and edits the same `.vvo` catalogues, and is
built for scripts and AI agents: every command writes one JSON document to stdout.

```bash
vvo volume list --db D:\Catalogues\backup.vvo
vvo search holiday --db D:\Catalogues\backup.vvo --format table
```

**Conventions**

- Every command that works on a catalogue takes `--db <path>`.
- Volumes, folders and records are named by id. Every listing returns the ids to act on.
- Progress goes to stderr; `--quiet` silences it. `--format table` prints for people instead of JSON.
- A failure is JSON too: `{ "error": { "code": "...", "message": "..." } }`.

**Commands**

| Command | Does |
|---|---|
| `db new`, `db info`, `db copy`, `db shrink`, `db export`, `db import` | Create, describe and look after catalogues |
| `volume list`, `volume create`, `volume update`, `volume delete` | Virtual volumes |
| `folder list`, `folder scan`, `folder update`, `folder copy`, `folder move`, `folder delete`, `folder rescan` | Folders in a volume |
| `ls`, `tree`, `stat`, `search` | Look inside the catalogued trees |
| `compare` | Diff two folders, or a folder against the disk |
| `add`, `rm` | Add files from disk under a folder, or remove records |
| `about` | Version, license and third-party notices |

`vvo <command> --help` describes each one.

**Exit codes**

| Code | Error code | Meaning |
|---|---|---|
| 0 | | Success |
| 1 | `error`, `invalid_database`, `invalid_file` | Something went wrong, the file is not a catalogue, or an export cannot be read. Also differences found, for `compare --exit-code` |
| 2 | `usage` | Bad arguments |
| 3 | `not_found` | No such catalogue, id, file or folder |
| 4 | `confirmation_required` | A destructive command run without a terminal (see below) |
| 5 | `database_busy` | The catalogue stayed locked by another program |
| 6 | `cancelled` | Declined at the prompt, or Ctrl+C |

**Confirmation**

Deleting, rescanning and replacing a file ask first, as the app does: the user has to type `delete`,
`update` or `replace` at the terminal. There is no flag to skip it. Run without a terminal, as agents
run commands, these commands change nothing and end in `confirmation_required`, carrying the warning
and the exact `command` for the user to run themselves. `compare <folder> --disk <path>` shows what a
rescan would change without asking anything.

Instructions to give an agent are in [docs/agents.md](docs/agents.md).

## File Association

Pass a `.vvo` file to the app to open it on launch:

```bash
VirtualVolumeOrganizer.exe D:\Catalogues\backup.vvo
```

Associate `.vvo` files with `VirtualVolumeOrganizer.exe` to open catalogues by double-clicking them.

## Credits

- Icons from [MahApps.Metro.IconPacks](https://github.com/MahApps/MahApps.Metro.IconPacks) via [IconPacks.Browser](https://github.com/MahApps/IconPacks.Browser).
- Third-party licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).


