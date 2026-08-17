using System.Collections.Generic;

namespace VVO.UI.ViewModels;

public record ShortcutRow(string Gesture, string Action);

/// <summary>
/// The keys the menu bar binds. Kept beside the menu that declares them, so a gesture added
/// there is added here too.
/// </summary>
public class ShortcutsDialogViewModel
{
    public IReadOnlyList<ShortcutRow> Shortcuts { get; } =
    [
        new("Ctrl+N", "New database"),
        new("Ctrl+O", "Open database"),
        new("Ctrl+Shift+S", "Save a copy of the database"),
        new("Ctrl+Shift+N", "New virtual volume"),
        new("Ctrl+Shift+A", "Scan a folder into the selected volume"),
        new("Ctrl+E", "Edit the selected folder"),
        new("Ctrl+D", "Duplicate the selected folder"),
        new("Ctrl+F", "Search the files of the open folder"),
        new("Ctrl+Z", "Undo"),
        new("Ctrl+Y", "Redo")
    ];
}
