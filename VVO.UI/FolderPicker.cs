using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VVO.UI;

/// <summary>
/// The picker a folder on disk is chosen through, whether to scan it or to compare a scanned
/// one against it.
/// </summary>
public static class FolderPicker
{
    /// <summary>
    /// Chooses a folder in place of the picker, taking the title it would have been shown
    /// under. A test has no picker to click through; null returns the folder the user did
    /// not pick.
    /// </summary>
    public static Func<string, string?>? Picking { get; set; }

    /// <summary>
    /// Chooses several folders in place of the multi-select picker. Null or an empty list is
    /// the folders the user did not pick.
    /// </summary>
    public static Func<string, IReadOnlyList<string>?>? PickingMany { get; set; }

    public static void Reset()
    {
        Picking = null;
        PickingMany = null;
    }

    public static async Task<string?> PickAsync(Visual owner, string title)
    {
        if (Picking != null)
            return Picking(title);

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public static async Task<IReadOnlyList<string>> PickManyAsync(Visual owner, string title)
    {
        if (PickingMany != null)
            return PickingMany(title) ?? [];

        if (Picking != null)
        {
            var one = Picking(title);
            return one == null ? [] : [one];
        }

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null)
            return [];

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = true
        });

        return folders
            .Select(folder => folder.TryGetLocalPath())
            .Where(path => path != null)
            .Cast<string>()
            .ToList();
    }
}
