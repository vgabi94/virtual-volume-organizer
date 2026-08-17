<p align="center">
  <img src="VVO.UI/Assets/vvo.svg" alt="Virtual Volume Organizer" width="128" height="128">
</p>

# Virtual Volume Organizer

Virtual Volume Organizer (VVO) is a desktop application for cataloguing offline and removable
storage. It indexes the contents of a drive or folder into a portable database, so that the
catalogue can be browsed, searched and compared long after the media itself has been disconnected —
external drives, archive disks and backup sets remain fully inspectable while shelved.

The application targets .NET 10 and is built with [Avalonia](https://avaloniaui.net/); each
catalogue is held in a single portable file.

## Build and publish

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Nothing else.

**Run from source**

```
dotnet run --project VVO.UI
```

**Publish a self-contained executable** — one file, no .NET runtime needed on the target machine:

```
dotnet publish VVO.UI/VVO.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish/win-x64
```

The result is `publish/win-x64/VirtualVolumeOrganizer.exe`, about 49 MB. For other platforms swap
the runtime identifier: `linux-x64`, `osx-arm64`, `win-arm64`.

> **Put the executable in a folder of its own before running it.** On first start it writes a
> `settings.json` next to itself.

`IncludeNativeLibrariesForSelfExtract` embeds the native Skia and HarfBuzz libraries into the single-file bundle; without it they remain external and a true single-file distribution is impossible.  
`EnableCompressionInSingleFile` compresses the assemblies, producing a smaller package at the expense of a slightly slower first start.  

Do **not** enable `PublishTrimmed`: `ViewLocator` resolves views by reflection on view-model type names, and the trimmer removes them.

**Tests**

```
dotnet test VVO.slnx
```

## Opening a catalogue by double-clicking it

The executable takes a `.vvo` path as its argument and starts with that catalogue open:

```
VirtualVolumeOrganizer.exe D:\Catalogues\music.vvo
```

That is all the shell needs, so associating the file type is enough to make double-clicking a
catalogue open it. On Windows, right-click any `.vvo` → **Open with** → **Choose another app** →
browse to `VirtualVolumeOrganizer.exe` and tick **Always use this app**.

Each double-click starts its own instance; the application does not hand the file to a copy of
itself that is already running.

## What it does

A scan traverses the selected directory recursively and records every file and folder it contains into a catalogue database. Once the scan
completes, the catalogue stands in for the original media: its tree can be browsed and searched, and
compared against either another catalogue or a live directory, without the source storage being
present.

Only metadata is captured. File contents are never read, hashed or duplicated, so the size of a
catalogue is governed by the number of files it describes rather than the volume of data they
occupy.

## Concepts

**Database** — one `.vvo` file (LiteDB) holding an entire catalogue.

**Virtual volume** — a named, coloured group inside a database, each with an icon of its own. A
volume is an organisational bucket, not a physical device.

**Folder entry and scanned tree** — the scanned tree is the file
records; the folder entry is one placement of that tree in a volume, with its own label,
description, icon and colour. Copying a folder into another volume adds an entry pointing at the
same tree, so a folder that appears in five volumes is still stored once.

## Features

**Scanning** — recursive, with progress and cancellation. Inaccessible directories are skipped and folder sizes are totalled from their contents as it goes.

**Browsing and searching** — a sidebar of volumes and folders beside an explorer pane with name,
size, type, location and timestamps.

**Comparing** — pick any two catalogued folders and see what differs, or compare a catalogued folder
against a live folder on disk to find out what changed since the scan. Results are marked added,
removed or changed, with the change attributed to size, modification time, or both. Whole subtrees
that are uniformly added, removed or unchanged collapse into a single row.

**Updating** — replace what a folder entry holds with a fresh scan of any folder on disk, typically
the same one once its media is reconnected. The differences are presented for approval before
anything is written, and the entry records the path it was last read from. Entries sharing a
scanned tree are updated together.

**Organising** — cut, copy, paste, duplicate and move folder entries between volumes; relabel and
describe them; give any volume or folder its own icon and colour.

**Undo and redo** — creating and editing volumes, editing folders, and copying, pasting and
duplicating folders can all be undone with `Ctrl+Z`. Deletions cannot; they collect the scanned tree
behind them, so they ask for confirmation instead. Neither can an update, which is why it shows what
it would change first.

**Import and export** — write the whole catalogue to JSON and read it back. Separately,
*Save a Copy* writes a consistent snapshot of the live database, and *Shrink Database* compacts the
file after large deletions.

## Keyboard shortcuts

| | |
|---|---|
| `Ctrl+N` | New database |
| `Ctrl+O` | Open database |
| `Ctrl+Shift+S` | Save a copy of the database |
| `Ctrl+Shift+N` | New virtual volume |
| `Ctrl+Shift+A` | Scan a folder into the selected volume |
| `Ctrl+E` | Edit the selected folder |
| `Ctrl+D` | Duplicate the selected folder |
| `Ctrl+F` | Search the files of the open folder |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |

## Where things are stored

Catalogues are saved wherever the user chooses. Preferences are written to `settings.json` in the same directory as the executable, and that file is
created on first run.

Nothing is installed and nothing is written to the registry or to a user profile directory, so a
published build is portable.

## Credits

Icons were taken through
[IconPacks.Browser](https://github.com/MahApps/IconPacks.Browser) from the sets bundled by
[MahApps.Metro.IconPacks](https://github.com/MahApps/MahApps.Metro.IconPacks).

[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) lists every third-party component — icon sets,
runtime and libraries — together with its licence.
