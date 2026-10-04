namespace VVO.Core;

/// <summary>
/// How a path inside a catalogued tree is written. The folder at the top of the tree heads it
/// the way a drive letter heads a Windows path, so a folder listed as 'test' reads
/// 'test:\sub\file'. This is not a path on disk: the physical one is on the folder entry.
/// </summary>
public static class CataloguePath
{
    public const char Separator = '\\';

    /// <summary>
    /// Why a volume name or folder label can't be used, or null when it can. A ':' or '\' in one
    /// would read as a deeper path: a volume 'C:\Backups' heads 'C:\Backups:\summer'.
    /// </summary>
    public static string? NameProblem(string? name) =>
        name != null && name.AsSpan().IndexOfAny(':', Separator) >= 0
            ? @"A name can't contain ':' or '\', which separate the parts of a catalogue path."
            : null;

    /// <summary>
    /// The top of a tree as a crumb reads it, the way a drive is written 'C:'.
    /// </summary>
    public static string Root(string name) => $"{name}:";

    /// <summary>
    /// The top of a tree as a path reads it, the way a drive root is written 'C:\'.
    /// </summary>
    public static string RootPath(string name) => $"{name}:{Separator}";

    public static string Combine(string parent, string name)
    {
        if (parent.Length == 0)
            return name;

        return parent[^1] == Separator ? parent + name : $"{parent}{Separator}{name}";
    }
}
