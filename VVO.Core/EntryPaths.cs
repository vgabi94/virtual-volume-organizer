using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Core;

/// <summary>
/// Where one folder entry puts the records of its tree, written the way the explorer writes
/// them. The volume heads the catalogue path the way a drive does and the folder, under the
/// title it is listed with, is the first step below it, so 'readme.md' in a folder listed as
/// 'Code' in the volume 'test' reads 'test:\Code\readme.md'. Two entries sharing a tree place
/// the same record differently, which is why this is per entry.
/// </summary>
public sealed class EntryPaths
{
    private readonly RootFolderMetadata _entry;
    private readonly string _volumeName;
    private readonly Dictionary<Guid, FileRecord> _records;
    private readonly Dictionary<Guid, (string Catalogue, string Physical)> _paths = new();

    /// <param name="records">The entry's tree, root included.</param>
    public EntryPaths(RootFolderMetadata entry, string volumeName, IEnumerable<FileRecord> records)
    {
        _entry = entry;
        _volumeName = volumeName;
        _records = records.ToDictionary(record => record.Id);

        if (!_records.TryGetValue(entry.TreeId, out var root))
        {
            throw new ArgumentException(
                $"The records do not contain the root folder '{entry.TreeId}' of the entry.", nameof(records));
        }

        Title = TitleOf(entry, root);
    }

    /// <summary>
    /// What the entry is listed as: its label, or the name it was scanned under.
    /// </summary>
    public string Title { get; }

    public static string TitleOf(RootFolderMetadata entry, FileRecord root) =>
        !string.IsNullOrWhiteSpace(entry.Label) ? entry.Label : root.Name;

    public string CataloguePathOf(Guid recordId) => PathsOf(recordId, 0).Catalogue;

    /// <summary>
    /// Where the record was on disk when it was scanned. Empty when the entry carries no path.
    /// </summary>
    public string PhysicalPathOf(Guid recordId) => PathsOf(recordId, 0).Physical;

    private (string Catalogue, string Physical) PathsOf(Guid recordId, int depth)
    {
        if (_paths.TryGetValue(recordId, out var cached))
            return cached;

        if (!_records.TryGetValue(recordId, out var record))
            throw new CatalogueItemNotFoundException($"There is no catalogue record '{recordId}' in this tree.", nameof(recordId));

        (string, string) paths;
        if (record.Id == _entry.TreeId)
        {
            paths = (CataloguePath.Combine(CataloguePath.RootPath(_volumeName), Title), _entry.Path);
        }
        else
        {
            // Deeper than the tree has records means the parents run in a circle
            if (record.ParentId is not { } parentId || !_records.ContainsKey(parentId) || depth > _records.Count)
            {
                throw new InvalidOperationException(
                    $"The catalogue record '{record.Id}' has lost its parent '{record.ParentId}' in this tree.");
            }

            var parent = PathsOf(parentId, depth + 1);
            paths = (
                CataloguePath.Combine(parent.Catalogue, record.Name),
                parent.Physical.Length == 0 ? string.Empty : Path.Combine(parent.Physical, record.Name));
        }

        _paths[recordId] = paths;
        return paths;
    }
}
