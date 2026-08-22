# Testing

```
dotnet test VVO.slnx
```

Two projects, split by what they need to run rather than by what they cover.

## VVO.Tests

xUnit v2 under VSTest. Core services against real LiteDB files in temporary paths, plus view models
and helpers that can be constructed without a UI.

## VVO.UiTests

xUnit v3 with `Avalonia.Headless.XUnit`, which is why the project is an executable rather than a
library. `HeadlessTestApp` builds the *real* `VVO.UI.App`, so the styles, themes and icon resources
under test are the shipped ones.

One headless session owns a UI thread for the whole assembly, so the assembly declares
`CollectionBehavior(DisableTestParallelization = true)`. Tests share a UI thread; they cannot be
parallelised.

`UiTestBase` gives each test a database of its own, a real shown `Window` for commands to open
their dialogs over, and restores the seams afterwards. It constructs the services directly rather
than through `ServiceConfiguration`, so a test wires only what it needs.

The seams the tests drive the application through, each a static hook the application sets nowhere
and a test sets deliberately:

| Seam | Stands in for |
|---|---|
| `Dialogs.Owner` | the window a dialog is shown over |
| `Dialogs.Answer` | clicking a dialog through, per dialog opened |
| `Dialogs.Told` | a message put to the user, captured as title and body |
| `DatabaseFiles.Picking` | the catalogue file picker |
| `FolderPicker.Picking` | the folder picker a scan starts from |
| `TextClipboard.Writing` | the system clipboard |
| `Theme.Applying` | applying a theme to the live application |
| `Settings(string)` | the settings file, so a test writes its own instead of the one beside the executable |

All but the last are static hooks a running application leaves at their defaults: null, meaning do
the real thing, except `Dialogs.Owner`, which defaults to the main window. `UiTestBase.Dispose`
calls `Reset()` on each holder, so one test cannot leak a stub into the next.

Nothing answers a dialog until a test says how. A command that opens one before then is a test that
forgot to arrange it, and the default is to cancel, which is the harmless reading.

`VVO.UI` grants `InternalsVisibleTo` to `VVO.UiTests` so the tests can build the same `AppBuilder`
`Program` does; `Program` itself is internal.
