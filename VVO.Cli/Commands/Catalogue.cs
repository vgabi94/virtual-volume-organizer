using VVO.Cli.Output;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Commands;

/// <summary>
/// Reads the commands share, against the catalogue the command has opened.
/// </summary>
public static class Catalogue
{
    public static Task<IReadOnlyCollection<RootFolderMetadata>> EntriesAsync(CommandContext context) =>
        context.Service<IDatabaseService>().ReadItemsAsync<RootFolderMetadata>();

    public static async Task<VirtualVolumeRecord> VolumeAsync(CommandContext context, Guid volumeId)
    {
        var volumes = await context.Service<IDatabaseService>()
            .FindItemsAsync<VirtualVolumeRecord>(volume => volume.Id == volumeId);

        return volumes.SingleOrDefault()
            ?? throw CliException.NotFound($"There is no virtual volume '{volumeId}'.");
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
}
