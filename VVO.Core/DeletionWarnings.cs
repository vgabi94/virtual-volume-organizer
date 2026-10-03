namespace VVO.Core;

public sealed record DeletionWarning(string Title, string ItemName, string Message);

/// <summary>
/// What the user is told before something is deleted, in the GUI's dialog and at the CLI's
/// prompt alike, so the two never describe the same deletion differently.
/// </summary>
public static class DeletionWarnings
{
    /// <summary>
    /// What the user types to go ahead, compared exactly.
    /// </summary>
    public const string ConfirmationWord = "delete";

    public static DeletionWarning ForFolder(string name)
    {
        return new DeletionWarning(
            "Delete Folder",
            name,
            "Deleting this folder removes it from its virtual volume along with every catalogued file "
            + "and folder under it. This cannot be undone.");
    }

    /// <param name="otherFolders">
    /// Other folder entries listing the same scanned tree, which lose the file as well.
    /// </param>
    public static DeletionWarning ForCatalogueFile(string name, int otherFolders = 0)
    {
        return new DeletionWarning(
            "Delete File",
            name,
            "Deleting this file removes it from the catalogue. The file on disk is left alone. "
            + AlsoFrom(otherFolders, "It goes")
            + "This cannot be undone.");
    }

    /// <param name="otherFolders">
    /// Other folder entries listing the same scanned tree, which lose the folder as well.
    /// </param>
    public static DeletionWarning ForCatalogueFolder(string name, int otherFolders = 0)
    {
        return new DeletionWarning(
            "Delete Folder",
            name,
            "Deleting this folder removes it from the catalogue along with every catalogued file "
            + "and folder under it. The files on disk are left alone. "
            + AlsoFrom(otherFolders, "It goes")
            + "This cannot be undone.");
    }

    /// <param name="otherFolders">
    /// Other folder entries listing the same scanned trees, which lose the items as well.
    /// </param>
    public static DeletionWarning ForCatalogueItems(int count, bool includesFolder, int otherFolders = 0)
    {
        var extra = includesFolder
            ? " Folders take every catalogued file and folder under them."
            : string.Empty;

        return new DeletionWarning(
            "Delete Items",
            $"{count} items",
            "Deleting these items removes them from the catalogue. The files on disk are left alone."
            + extra
            + " "
            + AlsoFrom(otherFolders, "They go")
            + "This cannot be undone.");
    }

    // A copy or a duplicate shares its scanned tree, so what goes from one goes from all of them
    private static string AlsoFrom(int otherFolders, string subjectAndVerb) => otherFolders switch
    {
        0 => string.Empty,
        1 => $"{subjectAndVerb} from the other folder listing the same scan too. ",
        _ => $"{subjectAndVerb} from the {otherFolders} other folders listing the same scan too. "
    };

    public static DeletionWarning ForVirtualVolume(string name, int folderCount)
    {
        var held = folderCount == 0
            ? "It holds no folders."
            : $"The {folderCount} {(folderCount == 1 ? "folder it holds is" : "folders it holds are")} deleted with it.";

        return new DeletionWarning(
            "Delete Virtual Volume",
            name,
            $"Deleting this virtual volume cannot be undone. {held}");
    }
}
