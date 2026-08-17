using System;
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

    public static void Reset() => Picking = null;

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
}
