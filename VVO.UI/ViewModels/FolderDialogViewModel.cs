using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VVO.Core.Models;

namespace VVO.UI.ViewModels;

public partial class FolderDialogViewModel : AppearanceDialogViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial string FolderName { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    public override bool CanConfirm => base.CanConfirm && !string.IsNullOrWhiteSpace(FolderName);

    /// <param name="scannedName">
    /// Shown when the folder carries no label of its own, and what clearing the name falls
    /// back to.
    /// </param>
    public FolderDialogViewModel(RootFolderMetadata entry, string scannedName)
        : base("Edit Folder", "Save", Choices(), entry.Icon, entry.Color)
    {
        FolderName = string.IsNullOrWhiteSpace(entry.Label) ? scannedName : entry.Label;
        Description = entry.Description ?? string.Empty;
    }

    private static IEnumerable<IconChoice> Choices()
    {
        return FolderIcons.Keys.Select(key =>
            new IconChoice(key, FolderIcons.Lookup(key), FolderIcons.IsFlipped(key)));
    }
}
