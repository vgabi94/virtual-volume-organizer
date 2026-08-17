using LiteDB;
using VVO.Core.Models;

namespace VVO.Core.Services;

public class VirtualVolumeService : IVirtualVolumeService
{
    private readonly IDatabaseService _databaseService;

    public VirtualVolumeService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public Task<IReadOnlyCollection<VirtualVolumeRecord>> GetVirtualVolumesAsync()
    {
        return _databaseService.ReadItemsAsync<VirtualVolumeRecord>();
    }

    public async Task<VirtualVolumeRecord> CreateVirtualVolumeAsync(string name, string icon, string? color = null)
    {
        RequireName(name);
        RequireIcon(icon);

        var record = new VirtualVolumeRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Icon = icon,
            Color = color
        };

        await _databaseService.InsertItemsAsync<VirtualVolumeRecord>([record]);

        return record;
    }

    public Task UpdateVirtualVolumeAsync(Guid virtualVolumeId, string name, string icon, string? color)
    {
        RequireName(name);
        RequireIcon(icon);

        return _databaseService.TransactionAsync(db =>
        {
            var volumes = Volumes(db);
            var volume = RequireVolume(volumes, virtualVolumeId, nameof(virtualVolumeId));

            volumes.Update(volume with { Name = name, Icon = icon, Color = color });
        });
    }

    public Task DeleteVirtualVolumeAsync(
        Guid virtualVolumeId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _databaseService.TransactionAsync(db =>
        {
            var volumes = Volumes(db);
            RequireVolume(volumes, virtualVolumeId, nameof(virtualVolumeId));

            cancellationToken.ThrowIfCancellationRequested();

            volumes.Delete(virtualVolumeId);
            Entries(db).DeleteMany(entry => entry.VirtualVolumeId == virtualVolumeId);

            CollectOrphanTrees(db, progress, cancellationToken);
        });
    }

    public Task RestoreVirtualVolumeAsync(VirtualVolumeRecord volume)
    {
        RequireName(volume.Name);
        RequireIcon(volume.Icon);

        return _databaseService.TransactionAsync(db =>
        {
            var volumes = Volumes(db);
            if (volumes.FindById(volume.Id) != null)
            {
                throw new ArgumentException(
                    $"Virtual volume '{volume.Id}' is already there.", nameof(volume));
            }

            volumes.Insert(volume);
        });
    }

    public Task<IReadOnlyCollection<RootFolderMetadata>> GetFoldersAsync(Guid virtualVolumeId)
    {
        return _databaseService.FindItemsAsync<RootFolderMetadata>(entry => entry.VirtualVolumeId == virtualVolumeId);
    }

    // Big enough that the batching costs nothing next to the write, small enough that a scan of
    // a system drive reports more than once
    private const int SaveBatchSize = 20_000;

    public async Task<RootFolderMetadata> AddFolderAsync(
        Guid virtualVolumeId,
        RootFolderMetadata scanned,
        IReadOnlyCollection<FileRecord> records,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireRoot(scanned, records, nameof(records));

        var entry = scanned with { VirtualVolumeId = virtualVolumeId };

        await _databaseService.TransactionAsync(db =>
        {
            RequireVolume(Volumes(db), virtualVolumeId, nameof(virtualVolumeId));

            Save(db, records, records.Count, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Entries(db).Insert(entry);
        });

        return entry;
    }

    public async Task<RootFolderMetadata> UpdateFolderContentsAsync(
        Guid entryId,
        RootFolderMetadata scanned,
        IReadOnlyCollection<FileRecord> records,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireRoot(scanned, records, nameof(records));

        RootFolderMetadata? updated = null;

        await _databaseService.TransactionAsync(db =>
        {
            var entries = Entries(db);
            var entry = RequireEntry(entries, entryId, nameof(entryId));

            cancellationToken.ThrowIfCancellationRequested();

            // One statement however large the tree is, so it has nothing to say for itself
            // while it runs
            progress?.Report("Removing what was catalogued...");

            var treeId = entry.TreeId;
            Files(db).DeleteMany(record => record.RootFolderId == treeId);

            Save(db, Rerooted(records, scanned.TreeId, treeId), records.Count, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            updated = entry with { Path = scanned.Path, LastScanned = scanned.LastScanned };
            entries.Update(updated);
        });

        return updated!;
    }

    /// <summary>
    /// Writes a scanned tree in batches, only so that there is something to count: unlike a
    /// scan, the total is known here, so this can say how far along it is.
    /// </summary>
    private void Save(
        LiteDatabase db,
        IEnumerable<FileRecord> records,
        int total,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var saved = 0;
        foreach (var batch in records.Chunk(SaveBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Files(db).InsertBulk(batch);
            saved += batch.Length;
            progress?.Report($"Saving to database... {saved:N0} of {total:N0}");
        }
    }

    /// <summary>
    /// Moves a freshly scanned tree onto the identity of the one it replaces. A scan mints ids
    /// of its own, and taking them as they come would leave every folder entry pointing at a
    /// tree that had just been deleted.
    /// </summary>
    private static IEnumerable<FileRecord> Rerooted(
        IEnumerable<FileRecord> records, Guid scannedTreeId, Guid treeId)
    {
        return records.Select(record => record with
        {
            Id = record.Id == scannedTreeId ? treeId : record.Id,
            RootFolderId = treeId,
            ParentId = record.ParentId == scannedTreeId ? treeId : record.ParentId
        });
    }

    private static void RequireRoot(
        RootFolderMetadata scanned, IReadOnlyCollection<FileRecord> records, string paramName)
    {
        if (records.All(record => record.Id != scanned.TreeId))
        {
            throw new ArgumentException(
                $"The records do not contain the root folder '{scanned.TreeId}' described by the given metadata.",
                paramName);
        }
    }

    public async Task<RootFolderMetadata> CopyFolderAsync(Guid entryId, Guid targetVirtualVolumeId, string? label = null)
    {
        RootFolderMetadata? copy = null;

        await _databaseService.TransactionAsync(db =>
        {
            var entries = Entries(db);
            var entry = RequireEntry(entries, entryId, nameof(entryId));
            RequireVolume(Volumes(db), targetVirtualVolumeId, nameof(targetVirtualVolumeId));

            copy = entry with
            {
                Id = Guid.NewGuid(),
                VirtualVolumeId = targetVirtualVolumeId,
                Label = label ?? entry.Label
            };

            entries.Insert(copy);
        });

        return copy!;
    }

    public Task MoveFolderAsync(Guid entryId, Guid targetVirtualVolumeId)
    {
        return _databaseService.TransactionAsync(db =>
        {
            var entries = Entries(db);
            var entry = RequireEntry(entries, entryId, nameof(entryId));
            RequireVolume(Volumes(db), targetVirtualVolumeId, nameof(targetVirtualVolumeId));

            entries.Update(entry with { VirtualVolumeId = targetVirtualVolumeId });
        });
    }

    public async Task<RootFolderMetadata> UpdateFolderAsync(
        Guid entryId, string? label, string? description, string? icon, string? color)
    {
        RootFolderMetadata? updated = null;

        await _databaseService.TransactionAsync(db =>
        {
            var entries = Entries(db);
            var entry = RequireEntry(entries, entryId, nameof(entryId));

            updated = entry with
            {
                Label = Trimmed(label),
                Description = Trimmed(description),
                Icon = Trimmed(icon),
                Color = Trimmed(color)
            };

            entries.Update(updated);
        });

        return updated!;
    }

    public Task RemoveFolderAsync(
        Guid entryId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _databaseService.TransactionAsync(db =>
        {
            var entries = Entries(db);
            RequireEntry(entries, entryId, nameof(entryId));

            cancellationToken.ThrowIfCancellationRequested();

            entries.Delete(entryId);
            CollectOrphanTrees(db, progress, cancellationToken);
        });
    }

    public Task RestoreFolderAsync(RootFolderMetadata entry)
    {
        return _databaseService.TransactionAsync(db =>
        {
            var entries = Entries(db);
            if (entries.FindById(entry.Id) != null)
            {
                throw new ArgumentException($"Folder entry '{entry.Id}' is already there.", nameof(entry));
            }

            RequireVolume(Volumes(db), entry.VirtualVolumeId, nameof(entry));

            if (Files(db).FindById(entry.TreeId) == null)
            {
                throw new ArgumentException(
                    $"The scanned tree '{entry.TreeId}' is gone, so the folder cannot be restored.",
                    nameof(entry));
            }

            entries.Insert(entry);
        });
    }

    /// <summary>
    /// Deletes the trees no folder entry points at any more. Runs inside the transaction of
    /// whatever left them behind, so the database never holds a tree nothing refers to.
    /// </summary>
    private void CollectOrphanTrees(
        LiteDatabase db,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var referenced = Entries(db).FindAll().Select(entry => entry.TreeId).ToHashSet();

        var files = Files(db);

        // Every tree is reachable through its single parentless record, so the trees on
        // disk can be listed without reading the records below them.
        var roots = files.Find(record => record.ParentId == null).ToList();

        var orphans = roots.Where(root => !referenced.Contains(root.Id)).ToList();

        // A tree goes in one statement, so this counts trees rather than the records inside
        // them: a single large tree is one step that cannot report from within
        var collected = 0;
        foreach (var root in orphans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var treeId = root.Id;
            files.DeleteMany(record => record.RootFolderId == treeId);
            progress?.Report($"Deleting files... {++collected} of {orphans.Count}");
        }
    }

    private ILiteCollection<VirtualVolumeRecord> Volumes(LiteDatabase db)
    {
        return db.GetCollection<VirtualVolumeRecord>(_databaseService.TableName<VirtualVolumeRecord>());
    }

    private ILiteCollection<RootFolderMetadata> Entries(LiteDatabase db)
    {
        return db.GetCollection<RootFolderMetadata>(_databaseService.TableName<RootFolderMetadata>());
    }

    private ILiteCollection<FileRecord> Files(LiteDatabase db)
    {
        return db.GetCollection<FileRecord>(_databaseService.TableName<FileRecord>());
    }

    // Blank and absent mean the same thing to every optional field, so only one of them is stored
    private static string? Trimmed(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A virtual volume needs a name.", nameof(name));
        }
    }

    private static void RequireIcon(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            throw new ArgumentException("A virtual volume needs an icon.", nameof(icon));
        }
    }

    private static VirtualVolumeRecord RequireVolume(
        ILiteCollection<VirtualVolumeRecord> volumes, Guid virtualVolumeId, string paramName)
    {
        var volume = volumes.FindById(virtualVolumeId);
        if (volume == null)
        {
            throw new ArgumentException($"There is no virtual volume '{virtualVolumeId}'.", paramName);
        }

        return volume;
    }

    private static RootFolderMetadata RequireEntry(
        ILiteCollection<RootFolderMetadata> entries, Guid entryId, string paramName)
    {
        var entry = entries.FindById(entryId);
        if (entry == null)
        {
            throw new ArgumentException($"There is no folder entry '{entryId}'.", paramName);
        }

        return entry;
    }
}
