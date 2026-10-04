using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VVO.Core;
using VVO.Core.Models;

namespace VVO.UI.ViewModels;

public partial class FolderDialogViewModel : AppearanceDialogViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(NameProblem))]
    public partial string FolderName { get; set; }

    private readonly string _scannedName;

    [ObservableProperty]
    public partial string Description { get; set; }

    // The scanned name stores no label, so a drive scanned as 'C:' can still be saved as it is
    public string? NameProblem =>
        FolderName?.Trim() == _scannedName ? null : CataloguePath.NameProblem(FolderName);

    public override bool CanConfirm =>
        base.CanConfirm && !string.IsNullOrWhiteSpace(FolderName) && NameProblem == null;

    /// <param name="scannedName">
    /// Shown when the folder carries no label of its own, and what clearing the name falls
    /// back to.
    /// </param>
    public FolderDialogViewModel(RootFolderMetadata entry, string scannedName)
        : base("Edit Folder", "Save", Choices(), entry.Icon, entry.Color)
    {
        _scannedName = scannedName;
        FolderName = string.IsNullOrWhiteSpace(entry.Label) ? scannedName : entry.Label;
        Description = entry.Description ?? string.Empty;
    }

    private static IEnumerable<IconChoice> Choices()
    {
        return FolderIcons.Keys.Select(key =>
            new IconChoice(key, FolderIcons.Lookup(key), FolderIcons.IsFlipped(key)));
    }
}
