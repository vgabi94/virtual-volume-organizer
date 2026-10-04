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

    public string CataloguePathOf(Guid recordId) => PathsOf(recordId).Catalogue;

    /// <summary>
    /// Where the record was on disk when it was scanned. Empty when the entry carries no path.
    /// </summary>
    public string PhysicalPathOf(Guid recordId) => PathsOf(recordId).Physical;

    private (string Catalogue, string Physical) PathsOf(Guid recordId)
    {
        if (!_records.TryGetValue(recordId, out var record))
            throw new CatalogueItemNotFoundException($"There is no catalogue record '{recordId}' in this tree.", nameof(recordId));

        // Up to the nearest folder already placed, then back down placing each one on the way. A
        // walk rather than recursion, which a deep enough chain would run out of stack for
        var below = new Stack<FileRecord>();
        var visited = new HashSet<Guid>();
        var current = record;

        while (!_paths.ContainsKey(current.Id))
        {
            if (current.Id == _entry.TreeId)
            {
                _paths[current.Id] = (CataloguePath.Combine(CataloguePath.RootPath(_volumeName), Title), _entry.Path);
                break;
            }

            // A folder met twice means the parents run in a circle
            if (!visited.Add(current.Id)
                || current.ParentId is not { } parentId
                || !_records.TryGetValue(parentId, out var parent))
            {
                throw new InvalidOperationException(
                    $"The catalogue record '{current.Id}' has lost its parent '{current.ParentId}' in this tree.");
            }

            below.Push(current);
            current = parent;
        }

        var paths = _paths[current.Id];
        while (below.TryPop(out var next))
        {
            paths = (
                CataloguePath.Combine(paths.Catalogue, next.Name),
                paths.Physical.Length == 0 ? string.Empty : Path.Combine(paths.Physical, next.Name));

            _paths[next.Id] = paths;
        }

        return paths;
    }
}
