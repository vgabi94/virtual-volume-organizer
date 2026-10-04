using System;
using CommunityToolkit.Mvvm.ComponentModel;
using VVO.Core;

namespace VVO.UI.ViewModels;

public partial class ConfirmDeleteDialogViewModel : ObservableObject
{
    public const string RequiredPhrase = DeletionWarnings.ConfirmationWord;

    public string Title { get; }
    public string ItemName { get; }
    public string Message { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial string Confirmation { get; set; } = string.Empty;

    // Compared exactly, case included: the typing is the whole point of the confirmation
    public bool CanDelete => string.Equals(Confirmation, RequiredPhrase, StringComparison.Ordinal);

    // The wording lives in Core, where the CLI's prompt reads it too
    private ConfirmDeleteDialogViewModel(DeletionWarning warning)
    {
        Title = warning.Title;
        ItemName = warning.ItemName;
        Message = warning.Message;
    }

    public static ConfirmDeleteDialogViewModel ForFolder(string name) =>
        new(DeletionWarnings.ForFolder(name));

    public static ConfirmDeleteDialogViewModel ForCatalogueFile(string name) =>
        new(DeletionWarnings.ForCatalogueFile(name));

    public static ConfirmDeleteDialogViewModel ForCatalogueFolder(string name) =>
        new(DeletionWarnings.ForCatalogueFolder(name));

    public static ConfirmDeleteDialogViewModel ForCatalogueItems(int count, bool includesFolder) =>
        new(DeletionWarnings.ForCatalogueItems(count, includesFolder));

    public static ConfirmDeleteDialogViewModel ForVirtualVolume(string name, int folderCount) =>
        new(DeletionWarnings.ForVirtualVolume(name, folderCount));
}
