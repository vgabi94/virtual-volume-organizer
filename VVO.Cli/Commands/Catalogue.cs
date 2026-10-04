using VVO.Cli.Contract;
using VVO.Cli.Output;
using VVO.Core;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

/// <summary>
/// Reads the commands share, against the catalogue the command has opened.
/// </summary>
public static class Catalogue
{
    /// <summary>
    /// The folder entries the GUI would list: those whose virtual volume and scanned tree are both
    /// still there. One that lost either is damage, and is left out of what is read, as the
    /// sidebar leaves it out.
    /// </summary>
    public static async Task<IReadOnlyCollection<RootFolderMetadata>> EntriesAsync(CommandContext context)
    {
        var database = context.Service<IDatabaseService>();
        var volumes = (await database.ReadItemsAsync<VirtualVolumeRecord>()).Select(volume => volume.Id).ToHashSet();
        var trees = (await RootsAsync(context)).Keys.ToHashSet();

        return (await database.ReadItemsAsync<RootFolderMetadata>())
            .Where(entry => volumes.Contains(entry.VirtualVolumeId) && trees.Contains(entry.TreeId))
            .ToList();
    }

    public static async Task<VirtualVolumeRecord> VolumeAsync(CommandContext context, Guid volumeId)
    {
        var volumes = await context.Service<IDatabaseService>()
            .FindItemsAsync<VirtualVolumeRecord>(volume => volume.Id == volumeId);

        return volumes.SingleOrDefault()
            ?? throw CliException.NotFound($"There is no virtual volume '{volumeId}'.");
    }

    public static async Task<RootFolderMetadata> EntryAsync(CommandContext context, Guid entryId)
    {
        var entries = await context.Service<IDatabaseService>()
            .FindItemsAsync<RootFolderMetadata>(entry => entry.Id == entryId);

        return entries.SingleOrDefault()
            ?? throw CliException.NotFound($"There is no folder entry '{entryId}'.");
    }

    /// <summary>
    /// A folder entry as the folder commands report it.
    /// </summary>
    public static async Task<FolderEntryDto> DescribeAsync(CommandContext context, RootFolderMetadata entry)
    {
        var treeId = entry.TreeId;
        var root = (await context.Service<IDatabaseService>().FindItemsAsync<FileRecord>(record => record.Id == treeId))
            .SingleOrDefault();

        // An entry whose tree is gone can still be deleted, and is described by what it still has
        return root == null
            ? FolderEntryDto.From(entry, new FileRecord { Id = treeId, Name = Path.GetFileName(entry.Path) }, 0)
            : FolderEntryDto.From(entry, root, await RecordCountAsync(context, treeId));
    }

    /// <summary>
    /// The root record of each tree, which carries the scanned name and the size of the whole.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, FileRecord>> RootsAsync(CommandContext context)
    {
        var roots = await context.Service<IDatabaseService>()
            .FindItemsAsync<FileRecord>(record => record.ParentId == null);

        return roots.ToDictionary(root => root.Id);
    }

    /// <summary>
    /// Files and folders in a tree, its root left out.
    /// </summary>
    public static Task<int> RecordCountAsync(CommandContext context, Guid treeId) =>
        context.Service<IDatabaseService>()
            .CountItemsAsync<FileRecord>(record => record.RootFolderId == treeId && record.ParentId != null);

    public static FileRecord RootOf(IReadOnlyDictionary<Guid, FileRecord> roots, RootFolderMetadata entry) =>
        roots.TryGetValue(entry.TreeId, out var root)
            ? root
            : throw new InvalidOperationException(
                $"The folder entry '{entry.Id}' points at a tree '{entry.TreeId}' the catalogue does not hold.");

    /// <summary>
    /// A record and the folder entries it is reached through. An entry id stands for the root
    /// of its tree, reached through that entry alone; a record id is reached through every entry
    /// standing on its tree.
    /// </summary>
    public static async Task<Located> LocateAsync(CommandContext context, Guid id)
    {
        var database = context.Service<IDatabaseService>();

        var listed = await EntriesAsync(context);

        var entry = listed.SingleOrDefault(item => item.Id == id);
        if (entry != null)
        {
            var roots = await database.FindItemsAsync<FileRecord>(record => record.Id == entry.TreeId);
            return new Located(RootOf(roots.ToDictionary(root => root.Id), entry), [entry]);
        }

        var found = (await database.FindItemsAsync<FileRecord>(record => record.Id == id)).SingleOrDefault()
            ?? throw CliException.NotFound($"There is no folder entry or catalogue record '{id}'.");

        return new Located(found, listed.Where(item => item.TreeId == found.RootFolderId).ToList());
    }

    /// <summary>
    /// The folders above a record, from the root of its tree down to its parent.
    /// </summary>
    public static async Task<IReadOnlyList<FileRecord>> AncestorsAsync(CommandContext context, FileRecord record)
    {
        var database = context.Service<IDatabaseService>();
        var ancestors = new List<FileRecord>();
        var visited = new HashSet<Guid> { record.Id };

        for (var parentId = record.ParentId; parentId is { } id;)
        {
            var parent = (await database.FindItemsAsync<FileRecord>(item => item.Id == id)).SingleOrDefault()
                ?? throw new InvalidOperationException($"The catalogue record '{record.Id}' has lost its parent '{id}'.");

            if (!visited.Add(parent.Id))
                throw new InvalidOperationException(
                    $"The folders above the catalogue record '{record.Id}' are each other's parents and never reach the top of the tree.");

            ancestors.Insert(0, parent);
            parentId = parent.ParentId;
        }

        return ancestors;
    }

    /// <summary>
    /// The given records together with every folder above them, read a level at a time so that
    /// records sharing folders cost a query per level rather than one per record. A folder that is
    /// gone is simply not there; <see cref="IsPlaced"/> tells which records reach their root.
    /// </summary>
    public static async Task<IReadOnlyCollection<FileRecord>> WithAncestorsAsync(
        CommandContext context, IEnumerable<FileRecord> records)
    {
        var database = context.Service<IDatabaseService>();
        var known = records.ToDictionary(record => record.Id);

        var missing = MissingParents(known, known.Values);
        while (missing.Count > 0)
        {
            var ids = missing;
            var parents = await database.FindItemsAsync<FileRecord>(record => ids.Contains(record.Id));

            foreach (var parent in parents)
            {
                known[parent.Id] = parent;
            }

            missing = MissingParents(known, parents);
        }

        return known.Values;
    }

    /// <summary>
    /// Whether every folder above the record is among <paramref name="known"/>, all the way up to
    /// the root of its tree.
    /// </summary>
    public static bool IsPlaced(FileRecord record, IReadOnlyDictionary<Guid, FileRecord> known)
    {
        var visited = new HashSet<Guid>();
        for (var current = record; current.ParentId is { } parentId; )
        {
            if (!visited.Add(current.Id) || !known.TryGetValue(parentId, out var parent))
                return false;

            current = parent;
        }

        return true;
    }

    private static List<Guid> MissingParents(Dictionary<Guid, FileRecord> known, IEnumerable<FileRecord> records) =>
        records
            .Where(record => record.ParentId is { } parentId && !known.ContainsKey(parentId))
            .Select(record => record.ParentId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Places records the way each of the given entries does.
    /// </summary>
    /// <param name="records">
    /// Every record to be placed, together with all the folders above them.
    /// </param>
    public static async Task<Placer> PlacerAsync(
        CommandContext context, IReadOnlyList<RootFolderMetadata> entries, IReadOnlyCollection<FileRecord> records)
    {
        var volumes = (await context.Service<IDatabaseService>().ReadItemsAsync<VirtualVolumeRecord>())
            .ToDictionary(volume => volume.Id, volume => volume.Name);

        return new Placer(entries
            .OrderBy(entry => entry.Id)
            .Select(entry => (entry, new EntryPaths(entry, volumes.GetValueOrDefault(entry.VirtualVolumeId, string.Empty), records)))
            .ToList());
    }

    public static IEnumerable<FileRecord> InExplorerOrder(IEnumerable<FileRecord> records) =>
        records
            .OrderByDescending(record => record.IsFolder)
            .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            // Names a case-sensitive disk keeps apart would otherwise come out in whatever order
            .ThenBy(record => record.Name, StringComparer.Ordinal);
}

public sealed record Located(FileRecord Record, IReadOnlyList<RootFolderMetadata> Entries);

public sealed class Placer(IReadOnlyList<(RootFolderMetadata Entry, EntryPaths Paths)> entries)
{
    public RecordDto Describe(FileRecord record) =>
        RecordDto.From(record, entries.Select(item => PlacementDto.From(item.Entry, item.Paths, record.Id)).ToList());

    /// <summary>
    /// The record once for each entry, carrying only the place that entry puts it.
    /// </summary>
    public IEnumerable<RecordDto> DescribeEach(FileRecord record) =>
        entries.Select(item => RecordDto.From(record, [PlacementDto.From(item.Entry, item.Paths, record.Id)]));
}
