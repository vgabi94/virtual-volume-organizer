using System.Linq.Expressions;
using System.Text;
using LiteDB;
using VVO.Core.Models;

namespace VVO.Core.Services;

public class DatabaseService : IDatabaseService
{
    // Left to itself LiteDB returns dates as local time, shifting their ticks and making
    // a stored record unequal to the one that was written.
    private static readonly BsonMapper Mapper = CreateMapper();

    public event EventHandler? Modified;

    // LiteDB holds the file exclusively for as long as an operation lasts, so two that
    // overlap fail outright. Each waits its turn, which also keeps a change of database
    // from landing in the middle of one.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string _dbPath = string.Empty;
    public string DbPath => _dbPath;

    private static BsonMapper CreateMapper()
    {
        var mapper = new BsonMapper();
        mapper.RegisterType(
            serialize: (DateTime value) => value.ToUniversalTime(),
            deserialize: bson => bson.AsDateTime.ToUniversalTime());

        return mapper;
    }

    private async Task<T> ExclusiveAsync<T>(Func<T> operation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(operation).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Every write closes its database and gives up its turn before announcing itself. LiteDB
    // checkpoints on dispose, so announcing any earlier would report a change the file does
    // not yet carry, and announcing before the turn is up would leave a handler that reads
    // the database it is being told about waiting on the write it is answering.
    private async Task ExclusiveWriteAsync(Action operation)
    {
        await ExclusiveAsync<object?>(() =>
        {
            operation();
            return null;
        }).ConfigureAwait(false);

        Modified?.Invoke(this, EventArgs.Empty);
    }

    public Task EnsureDatabaseReadyAsync(string dbPath, bool replace = false)
    {
        return ExclusiveWriteAsync(() =>
        {
            if (replace && File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            if (File.Exists(dbPath))
            {
                RequireCatalogue(dbPath);
            }

            using var db = new LiteDatabase(dbPath, Mapper);
            EnsureIndexes(db);

            // Taken up only once the file has been opened, so a failed open leaves the
            // application on the database it already had rather than on one it never read
            _dbPath = dbPath;
        });
    }

    // Written by LiteDB at the head of its first page
    private const string HeaderInfo = "** This is a LiteDB file **";
    private const int HeaderInfoOffset = 32;
    private const int PageSize = 8192;

    /// <summary>
    /// Refuses a file that is not a catalogue, before LiteDB is handed it. Anything shorter than
    /// one page it takes for a database about to be created and writes an empty one over, so a
    /// catalogue left half copied — or a file that merely happens to be named .vvo — is destroyed
    /// by being opened. A file of no length is what a save picker leaves behind and holds nothing
    /// to lose, so it is left to be filled in.
    /// </summary>
    private static void RequireCatalogue(string dbPath)
    {
        var length = new FileInfo(dbPath).Length;
        if (length == 0)
            return;

        if (length < PageSize || !HasHeader(dbPath))
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(dbPath)}' is not a VVO catalogue, or it is damaged.");
        }
    }

    private static bool HasHeader(string dbPath)
    {
        using var file = new FileStream(
            dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        file.Seek(HeaderInfoOffset, SeekOrigin.Begin);

        var head = new byte[HeaderInfo.Length];
        file.ReadExactly(head);

        return Encoding.UTF8.GetString(head) == HeaderInfo;
    }

    /// <summary>
    /// The indexes the database is queried through, one step each so that a shrink can run them
    /// singly and count them while the open path just runs the lot.
    /// </summary>
    private IEnumerable<Action> IndexSteps(LiteDatabase db)
    {
        var files = db.GetCollection<FileRecord>(TableName<FileRecord>());
        var virtualVolumes = db.GetCollection<VirtualVolumeRecord>(TableName<VirtualVolumeRecord>());
        var metadata = db.GetCollection<RootFolderMetadata>(TableName<RootFolderMetadata>());
        var dbMetadata = db.GetCollection<DatabaseMetadata>(TableName<DatabaseMetadata>());

        yield return () => files.EnsureIndex(x => x.Name);
        yield return () => files.EnsureIndex(x => x.RootFolderId);
        yield return () => files.EnsureIndex(x => x.ParentId);
        yield return () => virtualVolumes.EnsureIndex(x => x.Name);
        yield return () => metadata.EnsureIndex(x => x.Path);
        yield return () => metadata.EnsureIndex(x => x.VirtualVolumeId);
        yield return () => metadata.EnsureIndex(x => x.TreeId);
        yield return () => dbMetadata.EnsureIndex(x => x.Name);
    }

    /// <summary>
    /// Run on every open, so an index that is missing for any reason — including a shrink that
    /// was stopped before it put them all back — is simply built again from the documents.
    /// </summary>
    private void EnsureIndexes(LiteDatabase db)
    {
        foreach (var step in IndexSteps(db))
        {
            step();
        }
    }

    public string TableName<T>()
    {
        return typeof(T).Name;
    }

    // LiteDB checkpoints and closes its file at the end of every operation, so what is on
    // disk is always the whole database and a plain file copy is enough.
    public Task CopyToAsync(string targetPath)
    {
        return ExclusiveAsync<object?>(() =>
        {
            File.Copy(_dbPath, targetPath, overwrite: true);
            return null;
        });
    }

    public Task InsertItemsAsync<T>(IReadOnlyCollection<T> items)
    {
        return ExclusiveWriteAsync(() =>
        {
            using var db = new LiteDatabase(_dbPath, Mapper);
            db.GetCollection<T>(TableName<T>()).InsertBulk(items);
        });
    }

    public Task UpdateItemsAsync<T>(IReadOnlyCollection<T> items)
    {
        return ExclusiveWriteAsync(() =>
        {
            using var db = new LiteDatabase(_dbPath, Mapper);
            db.GetCollection<T>(TableName<T>()).Update(items);
        });
    }

    public Task RemoveItemsAsync<T>(IReadOnlyCollection<Guid> itemIds) where T : IHasId
    {
        return ExclusiveWriteAsync(() =>
        {
            using var db = new LiteDatabase(_dbPath, Mapper);
            db.GetCollection<IHasId>(TableName<T>()).DeleteMany(x => itemIds.Contains(x.Id));
        });
    }

    public Task RemoveItemsAsync<T>(Expression<Func<T, bool>> predicate)
    {
        return ExclusiveWriteAsync(() =>
        {
            using var db = new LiteDatabase(_dbPath, Mapper);
            db.GetCollection<T>(TableName<T>()).DeleteMany(predicate);
        });
    }

    public Task<IReadOnlyCollection<T>> ReadItemsAsync<T>()
    {
        return ExclusiveAsync<IReadOnlyCollection<T>>(() =>
        {
            using var db = new LiteDatabase(_dbPath, Mapper);
            var collection = db.GetCollection<T>(TableName<T>());
            return collection.FindAll().ToList();
        });
    }

    public Task<IReadOnlyCollection<T>> FindItemsAsync<T>(Expression<Func<T, bool>> predicate)
    {
        return ExclusiveAsync<IReadOnlyCollection<T>>(() =>
        {
            using var db = new LiteDatabase(_dbPath, Mapper);
            var collection = db.GetCollection<T>(TableName<T>());
            return collection.Find(predicate).ToList();
        });
    }

    public Task ShrinkDatabaseAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExclusiveWriteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var db = new LiteDatabase(_dbPath, Mapper);

            // Rebuild throws 'Detected loop in FindAll' rebuilding a secondary index over a
            // collection of more than a few thousand documents, which is every catalogue worth
            // shrinking. Present in 5.0.21 and still in the 6.0 pre-releases, and it happens
            // whatever the index is over. Taking them out first leaves it only the documents to
            // copy, and they are put back from those documents afterwards.
            DropSecondaryIndexes(db, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // The one step that cannot say anything while it runs or be stopped part way
            progress?.Report("Compacting the database...");
            db.Rebuild();

            RebuildIndexes(db, progress, cancellationToken);
        });
    }

    public string? ShrinkBackupPath()
    {
        if (string.IsNullOrEmpty(_dbPath))
            return null;

        var beside = Path.Combine(
            Path.GetDirectoryName(_dbPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(_dbPath)}-backup{Path.GetExtension(_dbPath)}");

        // Checked for rather than assumed: a name LiteDB stopped using would otherwise have the
        // application pointing at a file that is not there
        return File.Exists(beside) ? beside : null;
    }

    /// <summary>
    /// Drops every index but the primary key, which cannot be dropped and is not the one that
    /// trips the rebuild. Read back from the database rather than listed here, so an index this
    /// class does not know about is dropped too instead of being left to break the rebuild.
    /// </summary>
    private static void DropSecondaryIndexes(
        LiteDatabase db, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var indexes = db
            .Execute("SELECT $.collection, $.name FROM $indexes")
            .ToEnumerable()
            .Select(index => (Collection: index["collection"].AsString, Name: index["name"].AsString))
            .Where(index => index.Name != "_id")
            .ToList();

        var dropped = 0;
        foreach (var index in indexes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            db.GetCollection(index.Collection).DropIndex(index.Name);
            progress?.Report($"Removing indexes... {++dropped} of {indexes.Count}");
        }
    }

    /// <summary>
    /// Puts the indexes back one at a time, so the longest part of a shrink after the compaction
    /// can say how far along it is and can be stopped between them.
    /// </summary>
    private void RebuildIndexes(
        LiteDatabase db, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var steps = IndexSteps(db).ToList();

        var built = 0;
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            step();
            progress?.Report($"Rebuilding indexes... {++built} of {steps.Count}");
        }
    }

    public Task TransactionAsync(Func<LiteDatabase, Task> actionAsync)
    {
        return ExclusiveWriteAsync(() =>
        {
            // LiteDB ties a transaction to the thread that opened it: a commit issued from
            // anywhere else finds no transaction and the writes are dropped on dispose. Every
            // continuation the action awaits is therefore pumped back onto this thread.
            var previous = SynchronizationContext.Current;
            var transactionThread = new TransactionSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(transactionThread);

            try
            {
                using var db = new LiteDatabase(_dbPath, Mapper);

                db.BeginTrans();
                try
                {
                    transactionThread.RunToCompletion(actionAsync(db));
                    db.Commit();
                }
                catch (Exception)
                {
                    db.Rollback();
                    throw;
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });
    }

    public Task TransactionAsync(Action<LiteDatabase> action)
    {
        return TransactionAsync(db =>
        {
            action(db);
            return Task.CompletedTask;
        });
    }
}
