namespace VVO.Core;

/// <summary>
/// What a scan says when it could not read everything, and what a rescan asks before it writes,
/// in the GUI's dialogs and at the CLI's prompt alike.
/// </summary>
public static class ScanWarnings
{
    public const string SkippedTitle = "Some folders were not read";

    public const string RescanProposal =
        "Rescanning replaces what is catalogued for this folder with what was just scanned.";

    public static string DescribeSkipped(int skippedFolders, string path)
    {
        var folders = skippedFolders == 1 ? "1 folder" : $"{skippedFolders} folders";

        return $"{folders} under '{path}' could not be opened and were left out.\n\n"
               + "This is usually a permission the account does not have, or a protection "
               + "feature such as Controlled Folder Access blocking the folder.";
    }
}
