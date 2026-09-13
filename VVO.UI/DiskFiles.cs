using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VVO.UI;

/// <summary>
/// The picker ordinary files on disk are chosen through, to catalogue them under an open folder.
/// </summary>
public static class DiskFiles
{
    /// <summary>
    /// Chooses paths in place of the picker, taking the title it would have been shown under.
    /// A test has no picker to click through; null or an empty list is the files the user did
    /// not pick.
    /// </summary>
    public static Func<string, IReadOnlyList<string>?>? Picking { get; set; }

    public static void Reset() => Picking = null;

    public static async Task<IReadOnlyList<string>> PickManyAsync(Visual owner, string title)
    {
        if (Picking != null)
            return Picking(title) ?? [];

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null)
            return [];

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true
        });

        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path != null)
            .Cast<string>()
            .ToList();
    }
}
