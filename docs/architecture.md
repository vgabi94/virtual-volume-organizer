# Architecture

## Projects

| Project | Output | Purpose |
|---|---|---|
| `VVO.Core` | library | Models and services. Depends only on LiteDB. |
| `VVO.UI` | `VirtualVolumeOrganizer.exe` | Avalonia application, views and view models. |
| `VVO.Tests` | xUnit v2 | Core services, view models, helpers. |
| `VVO.UiTests` | xUnit v3, executable | The application driven through a headless Avalonia session. |

`VVO.Core` knows nothing about the UI. Icons and colours reach it as opaque strings —
`VirtualVolumeRecord.Icon` is a key the core never resolves — so no core type has to be changed to
add an icon or a theme.

The solution is `VVO.slnx`, the XML solution format. The `dotnet` CLI reads it directly; `msbuild`
needs a `.sln` and cannot be pointed at it.

## Composition

`App.OnFrameworkInitializationCompleted` is the composition root. `ServiceConfiguration` registers
every core service, `Settings`, and the four long-lived view models (`MainWindowViewModel`,
`SidebarViewModel`, `VolumeExplorerViewModel`, `StartPageViewModel`) as singletons. Dialog view
models are constructed where they are shown rather than resolved.

The theme is applied before the main window is built, so the window is never painted in one theme
and repainted in the other.

Views are found by name, not by registration: `ViewLocator` replaces `ViewModel` with `View` in the
type's full name and constructs the result. Two consequences — a view needs a parameterless
constructor, and `PublishTrimmed` cannot be enabled, because the trimmer removes views nothing
references statically.

## Persistence

One LiteDB file per catalogue, conventionally `.vvo`. Collections are named after the type
(`IDatabaseService.TableName<T>` returns `typeof(T).Name`), giving `FileRecord`,
`VirtualVolumeRecord`, `RootFolderMetadata` and `DatabaseMetadata`.

Indexes, rebuilt on every open so a missing one costs nothing:

- `FileRecord`: `Name`, `RootFolderId`, `ParentId`
- `VirtualVolumeRecord`: `Name`
- `RootFolderMetadata`: `Path`, `VirtualVolumeId`, `TreeId`
- `DatabaseMetadata`: `Name`

A custom `BsonMapper` forces every `DateTime` through UTC in both directions. Left alone, LiteDB
returns dates as local time, which shifts their ticks and makes a stored record unequal to the one
written.

Before LiteDB is handed a file, `DatabaseService` checks it carries LiteDB's header string at byte
offset 32. LiteDB treats anything shorter than one 8 KB page as a database it is about to create
and writes an empty one over it, so opening a half-copied catalogue — or an unrelated file named
`.vvo` — would destroy it. A zero-length file is exempt: that is what a save picker leaves behind.

### Concurrency

LiteDB holds the file exclusively for the length of an operation, so two overlapping operations
fail outright. Every call goes through a single `SemaphoreSlim`, which also stops a change of
database landing in the middle of one.

The `Modified` event is raised after the write's database has been closed — LiteDB checkpoints on
dispose, so raising it earlier would announce a change the file does not yet carry — and after the
semaphore is released, so a handler that reads the database is not waiting on the write it is
answering. It arrives on the thread that performed the write, not the caller's.

### Transactions

LiteDB ties a transaction to the thread that opened it: a commit issued from any other thread finds
no transaction and the writes are dropped on dispose. Since the service methods inside a
transaction are async, `TransactionAsync` installs a `TransactionSynchronizationContext` that pumps
every awaited continuation back onto the opening thread. This is why `IVirtualVolumeService` can
promise that a cancelled scan, update or delete leaves the database as it was.

## Data model

A scanned tree is a flat set of `FileRecord`s: each carries `ParentId` for its place in the tree and
`RootFolderId` for the tree it belongs to, so deleting a tree is one indexed query. Folder records
carry the total size of their contents rather than their own.

`RootFolderMetadata` is a *placement* of a tree in a virtual volume — label, description, icon,
colour, and the path and time it was last scanned — pointing at a tree by `TreeId`. Several entries
may share one tree, which is what makes copying and duplicating a folder cost a single record, and
what makes an update reach every entry standing on that tree at once.

Removing an entry collects its tree only when no other entry still points at it. That is why
deletion is not undoable while copy, paste and duplicate are: `UndoService` holds pairs of
`(execute, undo)` delegates, and there is nothing to put a collected tree back from.

## Scanning

`FileScannerService` subclasses `FileSystemEnumerator<FileRecord>` with
`RecurseSubdirectories = true`, so the whole tree is one enumeration rather than manual recursion.
The subclass exists to count directories the enumerator was refused — by permissions, or by
something such as Controlled Folder Access — which is reported as `ScanResult.SkippedFolders` and
is the only signal that a catalogue is short. Hidden and system entries are excluded by default and
are not refusals, so they never reach that count.

Timestamps are truncated to milliseconds and progress is reported at most every 200 ms.

## Comparison

`FolderCompareService` reports how the right tree differs from the left, collapsing any subtree that
is uniformly added, removed or unchanged into a single row carrying a `DescendantCount`; only mixed
subtrees are expanded.

`ChangeKind` covers size and modification time. Creation time is deliberately excluded: copying a
file changes it without the contents differing.

## Export

`DatabaseTransferService` writes the whole catalogue as one JSON document stamped with a
`FormatVersion`. Trees no folder entry refers to are left out, so an export carries what the
catalogue shows rather than everything the file is still holding.
