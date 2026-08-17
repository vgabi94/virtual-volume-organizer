using System.Linq.Expressions;
using LiteDB;
using VVO.Core.Models;

namespace VVO.Core.Services;

public interface IDatabaseService
{
    /// <summary>
    /// Raised after an operation that writes to the database file, once the file itself has
    /// been closed and is on disk. It is raised on the thread that carried out the write,
    /// which is not the caller's.
    /// </summary>
    event EventHandler? Modified;

    string DbPath { get; }

    /// <summary>
    /// Sets current working database file and ensures the tables are initialized. Anything the
    /// file already holds is cleared out first when <paramref name="replace"/> is asked for.
    /// </summary>
    Task EnsureDatabaseReadyAsync(string dbPath, bool replace = false);

    /// <summary>
    /// Writes the working database to a second file, waiting for whatever is using it to
    /// finish so the copy is of a whole database rather than one mid-write.
    /// </summary>
    Task CopyToAsync(string targetPath);

    /// <summary>
    /// Returns the table name of the corresponding type T.
    /// </summary>
    string TableName<T>();

    Task InsertItemsAsync<T>(IReadOnlyCollection<T> items);
    Task UpdateItemsAsync<T>(IReadOnlyCollection<T> items);
    Task RemoveItemsAsync<T>(IReadOnlyCollection<Guid> itemIds) where T : IHasId;
    Task RemoveItemsAsync<T>(Expression<Func<T, bool>> predicate);
    Task<IReadOnlyCollection<T>> ReadItemsAsync<T>();
    Task<IReadOnlyCollection<T>> FindItemsAsync<T>(Expression<Func<T, bool>> predicate);
    /// <summary>
    /// Rewrites the database without the space deleted records were holding. Cancellation is
    /// taken between the steps rather than during them: the compaction itself is one call into
    /// LiteDB that cannot be interrupted, so asking to stop part way through it takes effect
    /// once it returns. Stopping at any of those points leaves the records untouched and at
    /// worst an index missing, which the next open builds again.
    /// </summary>
    Task ShrinkDatabaseAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The copy of the database as it stood before the last shrink. LiteDB renames the old file
    /// aside rather than overwriting it, which is what makes an interrupted shrink harmless, and
    /// leaves it there afterwards. Null when there is none beside the open database.
    /// </summary>
    string? ShrinkBackupPath();

    /// <summary>
    /// Encapsulates the actionAsync in a transaction, ensuring that all operations within the action are atomic.
    /// </summary>
    Task TransactionAsync(Func<LiteDatabase, Task> actionAsync);
    
    /// <summary>
    /// Encapsulates the action in a transaction, ensuring that all operations within the action are atomic.
    /// </summary>
    Task TransactionAsync(Action<LiteDatabase> action);
}