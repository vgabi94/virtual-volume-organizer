using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VVO.UI;

/// <summary>
/// The file pickers a database is chosen through. Opening one is offered from the start page,
/// the sidebar and the menu bar, which all have to describe the same file type.
/// </summary>
public static class DatabaseFiles
{
    public static FilePickerFileType FileType { get; } = new("VVO Database")
    {
        Patterns = ["*.vvo"],
        AppleUniformTypeIdentifiers = ["public.data"]
    };

    /// <summary>
    /// Chooses a path in place of the picker, taking the suggested name and the extension asked
    /// for. A test has no picker to click through; null returns the path the user did not pick.
    /// </summary>
    public static Func<string, string, string?>? Picking { get; set; }

    public static void Reset() => Picking = null;

    public static async Task<string?> PickToOpenAsync(Visual owner)
    {
        if (Picking != null)
            return Picking(string.Empty, "vvo");

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select VVO File",
            AllowMultiple = false,
            FileTypeFilter = [FileType]
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public static async Task<string?> PickToSaveAsync(
        Visual owner, string suggestedName, string title = "Save a Copy")
    {
        if (Picking != null)
            return Picking(suggestedName, "vvo");

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null)
            return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "vvo",
            ShowOverwritePrompt = true,
            FileTypeChoices = [FileType]
        });

        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType JsonType { get; } = new("JSON Export")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"]
    };

    public static async Task<string?> PickJsonToOpenAsync(Visual owner)
    {
        if (Picking != null)
            return Picking(string.Empty, "json");

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Export File",
            AllowMultiple = false,
            FileTypeFilter = [JsonType]
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public static async Task<string?> PickJsonToSaveAsync(Visual owner, string suggestedName)
    {
        if (Picking != null)
            return Picking(suggestedName, "json");

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel == null)
            return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to JSON",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            FileTypeChoices = [JsonType]
        });

        return file?.TryGetLocalPath();
    }
}
