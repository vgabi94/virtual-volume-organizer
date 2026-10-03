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

    public static DeletionWarning ForCatalogueFile(string name)
    {
        return new DeletionWarning(
            "Delete File",
            name,
            "Deleting this file removes it from the catalogue. The file on disk is left alone. "
            + "This cannot be undone.");
    }

    public static DeletionWarning ForCatalogueFolder(string name)
    {
        return new DeletionWarning(
            "Delete Folder",
            name,
            "Deleting this folder removes it from the catalogue along with every catalogued file "
            + "and folder under it. The files on disk are left alone. This cannot be undone.");
    }

    public static DeletionWarning ForCatalogueItems(int count, bool includesFolder)
    {
        var extra = includesFolder
            ? " Folders take every catalogued file and folder under them."
            : string.Empty;

        return new DeletionWarning(
            "Delete Items",
            $"{count} items",
            "Deleting these items removes them from the catalogue. The files on disk are left alone."
            + extra
            + " This cannot be undone.");
    }

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
