using VVO.Core.Models;

namespace VVO.Core.Services;

/// <summary>
/// What an add left in the tree: the root as it now stands, and the top-level names that
/// were already there and so were not inserted.
/// </summary>
public sealed record AddRecordsResult(FileRecord Root, IReadOnlyList<string> Skipped);

public interface IVirtualVolumeService
{
    Task<IReadOnlyCollection<VirtualVolumeRecord>> GetVirtualVolumesAsync();

    Task<VirtualVolumeRecord> CreateVirtualVolumeAsync(string name, string icon, string? color = null);

    /// <summary>
    /// Overwrites the appearance of a virtual volume, leaving the folders it holds alone.
    /// </summary>
    Task UpdateVirtualVolumeAsync(Guid virtualVolumeId, string name, string icon, string? color);

    /// <summary>
    /// Deletes a virtual volume together with the folder entries it holds, and with the
    /// scanned trees those entries were the last to point at. Not reversible, though it is one
    /// transaction, so cancelling leaves the volume and everything under it where it was.
    /// </summary>
    Task DeleteVirtualVolumeAsync(
        Guid virtualVolumeId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts an empty virtual volume back under the identity it had, which is what redoing a
    /// creation that was undone needs.
    /// </summary>
    Task RestoreVirtualVolumeAsync(VirtualVolumeRecord volume);

    Task<IReadOnlyCollection<RootFolderMetadata>> GetFoldersAsync(Guid virtualVolumeId);

    /// <summary>
    /// Stores a freshly scanned tree and places it in the given virtual volume. The whole thing
    /// is one transaction, so cancelling part way puts nothing in the database rather than
    /// leaving half a tree behind.
    /// </summary>
    /// <returns>The stored entry, which carries the virtual volume it was placed in.</returns>
    Task<RootFolderMetadata> AddFolderAsync(
        Guid virtualVolumeId,
        RootFolderMetadata scanned,
        IReadOnlyCollection<FileRecord> records,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces what a folder holds with a freshly scanned tree, leaving how it is presented
    /// alone and recording where and when it was read. The tree keeps the identity it had, so
    /// every entry standing on it is brought up to date together rather than left pointing at
    /// records that are no longer there. One transaction, so cancelling part way leaves the
    /// folder holding what it was catalogued with.
    /// </summary>
    /// <returns>The entry as it now stands.</returns>
    Task<RootFolderMetadata> UpdateFolderContentsAsync(
        Guid entryId,
        RootFolderMetadata scanned,
        IReadOnlyCollection<FileRecord> records,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a second entry for an already stored folder, sharing its scanned tree.
    /// Duplicating within one virtual volume is the same call with its own volume as target.
    /// </summary>
    /// <param name="label">Overrides the label of the copy, keeping the original's when null.</param>
    Task<RootFolderMetadata> CopyFolderAsync(Guid entryId, Guid targetVirtualVolumeId, string? label = null);

    Task MoveFolderAsync(Guid entryId, Guid targetVirtualVolumeId);

    /// <summary>
    /// Overwrites how a folder is presented, leaving the scanned tree behind it alone.
    /// </summary>
    /// <param name="label">Null falls back to the name the folder was scanned under.</param>
    Task<RootFolderMetadata> UpdateFolderAsync(
        Guid entryId, string? label, string? description, string? icon, string? color);

    /// <summary>
    /// Removes a folder from its virtual volume, and with it the scanned tree behind it unless
    /// another folder still shares that tree. Not reversible, though it is one transaction, so
    /// cancelling leaves the folder where it was.
    /// </summary>
    Task RemoveFolderAsync(
        Guid entryId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes catalogued files and nested folders from their trees, and every record under a
    /// folder. Ancestor folder sizes shrink by the same amount. The tree root cannot be
    /// removed this way. Not reversible; one transaction, so cancelling leaves the records
    /// where they were.
    /// </summary>
    /// <returns>The root of each tree that changed, carrying its size as it now stands.</returns>
    Task<IReadOnlyList<FileRecord>> RemoveRecordsAsync(
        IReadOnlyCollection<Guid> recordIds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts catalogued files and nested folders under an existing folder, growing ancestor
    /// sizes by the same amount. A top-level name that is already there is skipped, along with
    /// everything under it. One transaction, so cancelling leaves the tree where it was.
    /// </summary>
    Task<AddRecordsResult> AddRecordsAsync(
        Guid parentId,
        IReadOnlyCollection<FileRecord> records,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a folder entry back under the identity it had, for redoing a copy or a duplicate
    /// that was undone. The tree it points at is still there, since only a delete collects one
    /// and a delete cannot be undone.
    /// </summary>
    Task RestoreFolderAsync(RootFolderMetadata entry);
}
