using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VVO.UI.ViewModels;

public record FolderChoice(Guid Id, string Name, string Path);

public partial class CompareTargetDialogViewModel : ObservableObject
{
    public IReadOnlyList<FolderChoice> Folders { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    public partial FolderChoice? SelectedFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    public partial string LiveFolderPath { get; set; } = string.Empty;

    public bool CanCompare => SelectedFolder != null || !string.IsNullOrWhiteSpace(LiveFolderPath);

    public CompareTargetDialogViewModel(IReadOnlyList<FolderChoice> folders)
    {
        Folders = folders;
    }

    // The two targets are mutually exclusive; choosing one abandons the other so that the
    // caller never has to decide which of them wins.
    partial void OnSelectedFolderChanged(FolderChoice? value)
    {
        if (value != null)
        {
            LiveFolderPath = string.Empty;
        }
    }

    partial void OnLiveFolderPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            SelectedFolder = null;
        }
    }

    [RelayCommand]
    private async Task BrowseFolder(Visual visual)
    {
        var path = await FolderPicker.PickAsync(visual, "Select Folder to Compare Against");

        if (!string.IsNullOrEmpty(path))
        {
            LiveFolderPath = path;
        }
    }
}
