using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VVO.UI.ViewModels;

public partial class ConfirmDeleteDialogViewModel : ObservableObject
{
    public const string RequiredPhrase = "delete";

    public string Title { get; }
    public string ItemName { get; }
    public string Message { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial string Confirmation { get; set; } = string.Empty;

    // Compared exactly, case included: the typing is the whole point of the confirmation
    public bool CanDelete => string.Equals(Confirmation, RequiredPhrase, StringComparison.Ordinal);

    private ConfirmDeleteDialogViewModel(string title, string itemName, string message)
    {
        Title = title;
        ItemName = itemName;
        Message = message;
    }

    public static ConfirmDeleteDialogViewModel ForFolder(string name)
    {
        return new ConfirmDeleteDialogViewModel(
            "Delete Folder",
            name,
            "Deleting this folder removes it from its virtual volume along with every catalogued file "
            + "and folder under it. This cannot be undone.");
    }

    public static ConfirmDeleteDialogViewModel ForCatalogueFile(string name)
    {
        return new ConfirmDeleteDialogViewModel(
            "Delete File",
            name,
            "Deleting this file removes it from the catalogue. The file on disk is left alone. "
            + "This cannot be undone.");
    }

    public static ConfirmDeleteDialogViewModel ForCatalogueFolder(string name)
    {
        return new ConfirmDeleteDialogViewModel(
            "Delete Folder",
            name,
            "Deleting this folder removes it from the catalogue along with every catalogued file "
            + "and folder under it. The files on disk are left alone. This cannot be undone.");
    }

    public static ConfirmDeleteDialogViewModel ForCatalogueItems(int count, bool includesFolder)
    {
        var extra = includesFolder
            ? " Folders take every catalogued file and folder under them."
            : string.Empty;

        return new ConfirmDeleteDialogViewModel(
            "Delete Items",
            $"{count} items",
            "Deleting these items removes them from the catalogue. The files on disk are left alone."
            + extra
            + " This cannot be undone.");
    }

    public static ConfirmDeleteDialogViewModel ForVirtualVolume(string name, int folderCount)
    {
        var held = folderCount == 0
            ? "It holds no folders."
            : $"The {folderCount} {(folderCount == 1 ? "folder it holds is" : "folders it holds are")} deleted with it.";

        return new ConfirmDeleteDialogViewModel(
            "Delete Virtual Volume",
            name,
            $"Deleting this virtual volume cannot be undone. {held}");
    }
}
