using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VVO.Core;
using VVO.Core.Models;

namespace VVO.UI.ViewModels;

/// <summary>
/// A catalogued folder a comparison starts from or is made against: the top of a scanned tree,
/// or any folder in it.
/// </summary>
/// <param name="Name">What the results window calls it.</param>
/// <param name="Path">Where it is on disk, empty when the tree was listed without a scanned path.</param>
public record ComparedFolder(Guid TreeId, Guid FolderId, string Name, string Path);

/// <summary>
/// A folder on offer to compare against. The listed folders head the tree and read their
/// subfolders the first time they are opened, so the dialog opens without reading every tree.
/// </summary>
public partial class FolderChoice : ObservableObject
{
    private readonly Func<Guid, Task<IReadOnlyCollection<FileRecord>>>? _readFolders;

    private Task? _reading;

    // The folders of the whole tree by parent, shared by every choice below a listed folder
    private ILookup<Guid?, FileRecord>? _subfolders;

    public Guid TreeId { get; }
    public Guid FolderId { get; }
    public string Name { get; }
    public string Path { get; }

    // Where it sits under the folder heading the tree, which is what the results are headed with
    public string Label { get; }

    public bool IsListed => FolderId == TreeId;
    public bool IsPlaceholder { get; }

    public AvaloniaList<FolderChoice> Children { get; } = new();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ComparedFolder Compared => new(TreeId, FolderId, Label, Path);

    /// <param name="readFolders">Reads the folder records of a tree. Without one the folder is offered on its own.</param>
    public FolderChoice(Guid treeId, string name, string path, Func<Guid, Task<IReadOnlyCollection<FileRecord>>>? readFolders = null)
        : this(treeId, treeId, name, path, name)
    {
        _readFolders = readFolders;

        // Stands in for subfolders not read yet, so the folder can be opened at all
        if (readFolders != null)
        {
            Children.Add(new FolderChoice(treeId, Guid.Empty, "Loading...", string.Empty, string.Empty, isPlaceholder: true));
        }
    }

    private FolderChoice(Guid treeId, Guid folderId, string name, string path, string label, bool isPlaceholder = false)
    {
        TreeId = treeId;
        FolderId = folderId;
        Name = name;
        Path = path;
        Label = label;
        IsPlaceholder = isPlaceholder;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = OpenAsync();
        }
    }

    /// <summary>
    /// Lists the subfolders, reading the tree first when this is a listed folder opened for the
    /// first time.
    /// </summary>
    public async Task OpenAsync()
    {
        if (_readFolders != null)
        {
            _reading ??= ReadAsync();
            await _reading;
        }

        // A level ahead, so each subfolder shows whether it can be opened in turn
        foreach (var child in Children.Where(child => child.Children.Count == 0 && child._subfolders != null))
        {
            child.Populate(child._subfolders!);
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            Populate((await _readFolders!(TreeId)).ToLookup(folder => folder.ParentId));
        }
        catch (Exception e)
        {
            Children.Clear();
            await Logger.ShowErrorAsync(e);
        }
    }

    private void Populate(ILookup<Guid?, FileRecord> subfolders)
    {
        _subfolders = subfolders;

        Children.Clear();
        Children.AddRange(subfolders[FolderId]
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(folder => new FolderChoice(
                TreeId,
                folder.Id,
                folder.Name,
                Path.Length == 0 ? string.Empty : System.IO.Path.Combine(Path, folder.Name),
                CataloguePath.Combine(Label, folder.Name))
            {
                _subfolders = subfolders
            }));
    }
}

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
        if (value is { IsPlaceholder: true })
        {
            SelectedFolder = null;
        }
        else if (value != null)
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
