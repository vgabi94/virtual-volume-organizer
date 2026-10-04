using System.Threading.Tasks;
using MsBox.Avalonia.Enums;
using VVO.Core;
using VVO.Core.Services;

namespace VVO.UI;

/// <summary>
/// What a scan says when it could not read everything under the folder it was given. A scan
/// carries on past a folder it is refused, so without this the catalogue would come out short
/// with nothing on screen to say so.
/// </summary>
public static class ScanWarning
{
    public const string Title = ScanWarnings.SkippedTitle;

    // The wording lives in Core, where the CLI reads it too
    public static string Describe(int skippedFolders, string path) =>
        ScanWarnings.DescribeSkipped(skippedFolders, path);

    public static Task TellIfShortAsync(ScanResult scan, string path)
    {
        return scan.SkippedFolders == 0
            ? Task.CompletedTask
            : Dialogs.TellAsync(Title, Describe(scan.SkippedFolders, path), Icon.Warning);
    }
}
