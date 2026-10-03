using VVO.Core;
using VVO.Core.Models;

namespace VVO.Cli.Contract;

// What agents read. Renaming or removing a member breaks them; the snapshot tests are there so
// that doing it is a decision rather than an accident.

public sealed record VolumeDto(Guid Id, string Name, string Icon, string? Color, int FolderCount)
{
    public static VolumeDto From(VirtualVolumeRecord volume, int folderCount) =>
        new(volume.Id, volume.Name, volume.Icon, volume.Color, folderCount);
}

/// <param name="Title">What the folder is listed as: its label, or the name it was scanned under.</param>
/// <param name="Path">Where the folder was on disk when it was last scanned.</param>
/// <param name="Size">Bytes in the whole tree.</param>
/// <param name="RecordCount">Files and folders in the tree, the folder itself left out.</param>
public sealed record FolderEntryDto(
    Guid Id,
    Guid VolumeId,
    Guid TreeId,
    string Title,
    string? Label,
    string? Description,
    string Path,
    string? Icon,
    string? Color,
    DateTime LastScanned,
    long Size,
    int RecordCount)
{
    public static FolderEntryDto From(RootFolderMetadata entry, FileRecord root, int recordCount) =>
        new(
            entry.Id,
            entry.VirtualVolumeId,
            entry.TreeId,
            EntryPaths.TitleOf(entry, root),
            entry.Label,
            entry.Description,
            entry.Path,
            entry.Icon,
            entry.Color,
            entry.LastScanned,
            root.Size,
            recordCount);
}

/// <summary>
/// Where one folder entry places a record. A tree can stand behind several entries, so a
/// record can be in several places at once.
/// </summary>
/// <param name="PhysicalPath">Empty when the entry carries no path on disk.</param>
public sealed record PlacementDto(Guid EntryId, string CataloguePath, string PhysicalPath)
{
    public static PlacementDto From(RootFolderMetadata entry, EntryPaths paths, Guid recordId) =>
        new(entry.Id, paths.CataloguePathOf(recordId), paths.PhysicalPathOf(recordId));
}

/// <param name="TreeId">The root folder record of the tree this record belongs to.</param>
/// <param name="Size">Bytes, totalled over everything below for a folder.</param>
public sealed record RecordDto(
    Guid Id,
    Guid? ParentId,
    Guid TreeId,
    bool IsFolder,
    string Name,
    long Size,
    DateTime Created,
    DateTime Modified,
    IReadOnlyList<PlacementDto> Entries)
{
    public static RecordDto From(FileRecord record, IReadOnlyList<PlacementDto> entries) =>
        new(
            record.Id,
            record.ParentId,
            record.RootFolderId,
            record.IsFolder,
            record.Name,
            record.Size,
            record.Created,
            record.Modified,
            entries);
}

/// <summary>
/// One side of a compared pair. A side read from disk was never stored, so it has no id.
/// </summary>
public sealed record ComparedRecordDto(Guid? Id, bool IsFolder, string Name, long Size, DateTime Created, DateTime Modified)
{
    public static ComparedRecordDto? From(FileRecord? record, bool stored) =>
        record == null
            ? null
            : new(stored ? record.Id : null, record.IsFolder, record.Name, record.Size, record.Created, record.Modified);
}

/// <param name="Changes">What differs about a changed file: "size", "modified". Empty otherwise.</param>
/// <param name="DescendantCount">Entries under a subtree that collapsed into this one row.</param>
public sealed record ComparisonRowDto(
    string RelativePath,
    ComparisonStatus Status,
    IReadOnlyList<string> Changes,
    int? DescendantCount,
    ComparedRecordDto? Left,
    ComparedRecordDto? Right)
{
    public static ComparisonRowDto From(ComparisonResult result, bool leftStored, bool rightStored) =>
        new(
            result.RelativePath,
            result.Status,
            ChangesOf(result.Changes),
            result.DescendantCount,
            ComparedRecordDto.From(result.Left, leftStored),
            ComparedRecordDto.From(result.Right, rightStored));

    // A list rather than the flags enum's "Size, Modified", which an agent would have to split
    private static IReadOnlyList<string> ChangesOf(ChangeKind changes)
    {
        var list = new List<string>();

        if (changes.HasFlag(ChangeKind.Size))
            list.Add("size");

        if (changes.HasFlag(ChangeKind.Modified))
            list.Add("modified");

        return list;
    }
}
