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

# Run tests
dotnet test VVO.slnx
```

### Self-Contained Binary

Build a single-file executable with no runtime dependency:

```bash
dotnet publish VVO.UI/VVO.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish/win-x64
```

Replace `win-x64` with `linux-x64`, `osx-arm64`, or `win-arm64` as needed.

> **Note**: Do not enable `PublishTrimmed`—views are resolved via reflection. The app is portable and writes `settings.json` next to the binary.

## CLI & File Association

Pass a `.vvo` file to open it on launch:

```bash
VirtualVolumeOrganizer.exe D:\Catalogues\backup.vvo
```

Associate `.vvo` files with `VirtualVolumeOrganizer.exe` to open catalogues by double-clicking them.

## Credits

- Icons from [MahApps.Metro.IconPacks](https://github.com/MahApps/MahApps.Metro.IconPacks) via [IconPacks.Browser](https://github.com/MahApps/IconPacks.Browser).
- Third-party licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).


