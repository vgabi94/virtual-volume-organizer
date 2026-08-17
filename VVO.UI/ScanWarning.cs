using System.Threading.Tasks;
using MsBox.Avalonia.Enums;
using VVO.Core.Services;

namespace VVO.UI;

/// <summary>
/// What a scan says when it could not read everything under the folder it was given. A scan
/// carries on past a folder it is refused, so without this the catalogue would come out short
/// with nothing on screen to say so.
/// </summary>
public static class ScanWarning
{
    public const string Title = "Some folders were not read";

    public static string Describe(int skippedFolders, string path)
    {
        var folders = skippedFolders == 1 ? "1 folder" : $"{skippedFolders} folders";

        return $"{folders} under '{path}' could not be opened and were left out.\n\n"
               + "This is usually a permission the account does not have, or a protection "
               + "feature such as Controlled Folder Access blocking the folder.";
    }

    public static Task TellIfShortAsync(ScanResult scan, string path)
    {
        return scan.SkippedFolders == 0
            ? Task.CompletedTask
            : Dialogs.TellAsync(Title, Describe(scan.SkippedFolders, path), Icon.Warning);
    }
}
