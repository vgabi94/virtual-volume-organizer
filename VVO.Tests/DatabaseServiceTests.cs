using LiteDB;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _service;
    private readonly List<string> _spilled = [];

    public DatabaseServiceTests()
    {
        // Use a unique database file for each test run to ensure isolation
        _dbPath = SpilledPath();
        _service = new DatabaseService();
        _service.EnsureDatabaseReadyAsync(_dbPath).GetAwaiter().GetResult();
    }

    private string SpilledPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
        _spilled.Add(path);

        return path;
    }

    public void Dispose()
    {
        // A shrink leaves the pre-shrink file beside the database, which is litter here
        foreach (var path in _spilled.Append(_service.ShrinkBackupPath()))
        {
            if (path != null && File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    private class TestItem : IHasId
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void EnsureDatabaseReady_CreatesDatabase()
    {
        Assert.True(File.Exists(_dbPath));
    }

    #region One operation at a time

    // LiteDB holds the file exclusively, so anything that overlapped used to fail outright
    // with "the process cannot access the file".

    [Fact]
    public async Task OverlappingOperations_DoNotCollideOverTheFile()
    {
        var work = new List<Task>();

        for (var i = 0; i < 10; i++)
        {
            var item = new TestItem { Id = Guid.NewGuid(), Name = $"Item {i}" };

            work.Add(_service.InsertItemsAsync(new[] { item }));
            work.Add(_service.ReadItemsAsync<TestItem>());
            work.Add(_service.FindItemsAsync<TestItem>(x => x.Name == "Item 1"));
            work.Add(_service.UpdateItemsAsync(new[] { item }));
        }

        await Task.WhenAll(work);

        Assert.Equal(10, (await _service.ReadItemsAsync<TestItem>()).Count);
    }

    [Fact]
    public async Task OpeningAnotherDatabase_WaitsForTheWorkAlreadyRunning()
    {
        var other = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");

        try
        {
            var bulk = Enumerable.Range(0, 500)
                .Select(i => new TestItem { Id = Guid.NewGuid(), Name = $"Item {i}" })
                .ToList();

            var writing = _service.InsertItemsAsync(bulk);
            var opening = _service.EnsureDatabaseReadyAsync(other);

            await Task.WhenAll(writing, opening);

            // The write went to the database it was issued against, and the new one is now
            // the working database rather than a half-written mixture of the two
            Assert.Equal(other, _service.DbPath);
            Assert.Empty(await _service.ReadItemsAsync<TestItem>());

            await _service.EnsureDatabaseReadyAsync(_dbPath);
            Assert.Equal(500, (await _service.ReadItemsAsync<TestItem>()).Count);
        }
        finally
        {
            try { File.Delete(other); } catch { }
        }
    }

    [Fact]
    public async Task EnsureDatabaseReady_ReplaceClearsWhatTheFileHeld()
    {
        await _service.InsertItemsAsync(new[] { new TestItem { Id = Guid.NewGuid(), Name = "Item 1" } });

        await _service.EnsureDatabaseReadyAsync(_dbPath, replace: true);

        Assert.Empty(await _service.ReadItemsAsync<TestItem>());
    }

    #endregion

    #region Refusing what is not a catalogue

    private const int OnePage = 8192;

    /// <summary>A file of the given content, cleared away with the rest of the test's leavings.</summary>
    private string FileHolding(byte[] content)
    {
        var path = SpilledPath();
        File.WriteAllBytes(path, content);

        return path;
    }

    private string FileHolding(string content)
    {
        var path = SpilledPath();
        File.WriteAllText(path, content);

        return path;
    }

    // LiteDB takes anything shorter than a page for a database it is about to create and writes
    // an empty one over it, so opening one of these used to destroy it
    [Fact]
    public async Task ASmallFileThatIsNotACatalogueIsRefusedRatherThanWrittenOver()
    {
        var path = FileHolding("this is not a database");

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.EnsureDatabaseReadyAsync(path));

        Assert.Equal("this is not a database", File.ReadAllText(path));
    }

    // Which is what an interrupted copy off a drive leaves behind
    [Fact]
    public async Task ACatalogueCutShortOfOnePageIsRefusedRatherThanWrittenOver()
    {
        await _service.InsertItemsAsync(new[] { new TestItem { Id = Guid.NewGuid(), Name = "Item 1" } });

        var truncated = FileHolding(File.ReadAllBytes(_dbPath)[..4096]);
        var before = File.ReadAllBytes(truncated);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.EnsureDatabaseReadyAsync(truncated));

        Assert.Equal(before, File.ReadAllBytes(truncated));
    }

    [Fact]
    public async Task AFileLongEnoughButHeadedBySomethingElseIsRefused()
    {
        var path = FileHolding(new string('a', OnePage));

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.EnsureDatabaseReadyAsync(path));
    }

    // The save picker creates the file before anything has been written to it
    [Fact]
    public async Task AFileOfNoLengthIsFilledInRatherThanRefused()
    {
        var path = FileHolding(Array.Empty<byte>());

        await _service.EnsureDatabaseReadyAsync(path);

        Assert.Equal(path, _service.DbPath);
    }

    // Nothing was read, so the application is still standing on the database it already had
    [Fact]
    public async Task ARefusedFileLeavesTheServiceOnTheDatabaseItAlreadyHad()
    {
        await _service.InsertItemsAsync(new[] { new TestItem { Id = Guid.NewGuid(), Name = "Item 1" } });
        var path = FileHolding("this is not a database");

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.EnsureDatabaseReadyAsync(path));

        Assert.Equal(_dbPath, _service.DbPath);
        Assert.Equal("Item 1", (await _service.ReadItemsAsync<TestItem>()).Single().Name);
    }

    // Import owns the file it lands in, so it clears the way rather than being refused
    [Fact]
    public async Task ReplacingAFileThatIsNotACatalogueIsAllowed()
    {
        var path = FileHolding("this is not a database");

        await _service.EnsureDatabaseReadyAsync(path, replace: true);

        Assert.Equal(path, _service.DbPath);
    }

    #endregion

    #region Copying the file

    [Fact]
    public async Task CopyTo_WritesADatabaseHoldingWhatThisOneHolds()
    {
        var target = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");

        try
        {
            await _service.InsertItemsAsync(new[] { new TestItem { Id = Guid.NewGuid(), Name = "Item 1" } });

            await _service.CopyToAsync(target);

            var copy = new DatabaseService();
            await copy.EnsureDatabaseReadyAsync(target);
            Assert.Equal("Item 1", (await copy.ReadItemsAsync<TestItem>()).Single().Name);
        }
        finally
        {
            try { File.Delete(target); } catch { }
        }
    }

    [Fact]
    public async Task CopyTo_WaitsForAWriteRatherThanCatchingItHalfDone()
    {
        var target = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");

        try
        {
            var bulk = Enumerable.Range(0, 500)
                .Select(i => new TestItem { Id = Guid.NewGuid(), Name = $"Item {i}" })
                .ToList();

            var writing = _service.InsertItemsAsync(bulk);
            var copying = _service.CopyToAsync(target);

            await Task.WhenAll(writing, copying);

            var copy = new DatabaseService();
            await copy.EnsureDatabaseReadyAsync(target);
            Assert.Equal(500, (await copy.ReadItemsAsync<TestItem>()).Count);
        }
        finally
        {
            try { File.Delete(target); } catch { }
        }
    }

    [Fact]
    public async Task CopyTo_DoesNotAnnounceAWrite()
    {
        var target = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
        var raised = 0;
        _service.Modified += (_, _) => Interlocked.Increment(ref raised);

        try
        {
            await _service.CopyToAsync(target);

            Assert.Equal(0, raised);
        }
        finally
        {
            try { File.Delete(target); } catch { }
        }
    }

    #endregion

    [Fact]
    public async Task InsertItemsAsync_InsertsItems()
    {
        var items = new List<TestItem>
        {
            new TestItem { Id = Guid.NewGuid(), Name = "Item 1" },
            new TestItem { Id = Guid.NewGuid(), Name = "Item 2" }
        };

        await _service.InsertItemsAsync(items);

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        var count = collection.Count();
        Assert.Equal(2, count);
        Assert.NotNull(collection.FindById(items[0].Id));
        Assert.NotNull(collection.FindById(items[1].Id));
    }

    [Fact]
    public async Task UpdateItemsAsync_UpdatesItems()
    {
        var item = new TestItem { Id = Guid.NewGuid(), Name = "Item 1" };
        await _service.InsertItemsAsync(new[] { item });

        item.Name = "Updated Item 1";
        await _service.UpdateItemsAsync(new[] { item });

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        var updatedItem = collection.FindById(item.Id);
        Assert.Equal("Updated Item 1", updatedItem.Name);
    }

    [Fact]
    public async Task RemoveItemsAsync_RemovesItems()
    {
        var item1 = new TestItem { Id = Guid.NewGuid(), Name = "Item 1" };
        var item2 = new TestItem { Id = Guid.NewGuid(), Name = "Item 2" };
        await _service.InsertItemsAsync(new[] { item1, item2 });

        await _service.RemoveItemsAsync<TestItem>(new[] { item1.Id });

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        Assert.Equal(1, collection.Count());
        Assert.Null(collection.FindById(item1.Id));
        Assert.NotNull(collection.FindById(item2.Id));
    }

    [Fact]
    public async Task RemoveItemsAsync_WithPredicate_RemovesMatchingItems()
    {
        var item1 = new TestItem { Id = Guid.NewGuid(), Name = "Target" };
        var item2 = new TestItem { Id = Guid.NewGuid(), Name = "Other" };
        var item3 = new TestItem { Id = Guid.NewGuid(), Name = "Target" };
        await _service.InsertItemsAsync(new[] { item1, item2, item3 });

        await _service.RemoveItemsAsync<TestItem>(x => x.Name == "Target");

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        Assert.Equal(1, collection.Count());
        Assert.Null(collection.FindById(item1.Id));
        Assert.NotNull(collection.FindById(item2.Id));
        Assert.Null(collection.FindById(item3.Id));
    }

    [Fact]
    public async Task TransactionAsync_CommitsOnSuccess()
    {
        var item = new TestItem { Id = Guid.NewGuid(), Name = "Tx Item" };

        await _service.TransactionAsync(async db =>
        {
            var collection = db.GetCollection<TestItem>("TestItem");
            collection.Insert(item);
            await Task.CompletedTask;
        });

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        Assert.NotNull(collection.FindById(item.Id));
    }

    [Fact]
    public async Task TransactionAsync_RollsBackOnFailure()
    {
        var item = new TestItem { Id = Guid.NewGuid(), Name = "Tx Item" };

        await Assert.ThrowsAsync<Exception>(() => _service.TransactionAsync(async db =>
        {
            var collection = db.GetCollection<TestItem>("TestItem");
            collection.Insert(item);
            await Task.CompletedTask;
            throw new Exception("Test failure");
        }));

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        Assert.Null(collection.FindById(item.Id));
    }

    // A completed task resumes inline and so never leaves the transaction's thread. Only a
    // delay yields for real, which is what a transaction has to survive.
    [Fact]
    public async Task TransactionAsync_WithAsyncAction_CommitsOnSuccess()
    {
        var item = new TestItem { Id = Guid.NewGuid(), Name = "Async Tx Item" };

        await _service.TransactionAsync(async db =>
        {
            var collection = db.GetCollection<TestItem>("TestItem");
            collection.Insert(item);
            await Task.Delay(2);
        });

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        Assert.NotNull(collection.FindById(item.Id));
    }

    // LiteDB ties a transaction to the thread that opened it, so a commit that lands anywhere
    // else silently drops the writes
    [Fact]
    public async Task TransactionAsync_ResumesOnTheThreadThatOpenedTheTransaction()
    {
        var opened = 0;
        var resumed = 0;

        await _service.TransactionAsync(async _ =>
        {
            opened = Environment.CurrentManagedThreadId;
            await Task.Delay(2);
            resumed = Environment.CurrentManagedThreadId;
        });

        Assert.Equal(opened, resumed);
    }

    [Fact]
    public async Task TransactionAsync_WithAsyncAction_RollsBackOnFailureAfterAwaiting()
    {
        var item = new TestItem { Id = Guid.NewGuid(), Name = "Async Tx Item" };

        await Assert.ThrowsAsync<Exception>(() => _service.TransactionAsync(async db =>
        {
            var collection = db.GetCollection<TestItem>("TestItem");
            collection.Insert(item);
            await Task.Delay(2);
            throw new Exception("Test failure");
        }));

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        Assert.Null(collection.FindById(item.Id));
    }

    [Fact]
    public async Task TransactionAsync_WithSyncAction_CommitsOnSuccess()
    {
        var item = new TestItem { Id = Guid.NewGuid(), Name = "Sync Tx Item" };

        await _service.TransactionAsync(db =>
        {
            var collection = db.GetCollection<TestItem>("TestItem");
            collection.Insert(item);
        });

        using var db = new LiteDB.LiteDatabase(_dbPath);
        var collection = db.GetCollection<TestItem>("TestItem");
        Assert.NotNull(collection.FindById(item.Id));
    }

    [Fact]
    public async Task ReadItemsAsync_ReturnsAllItems()
    {
        var items = new List<TestItem>
        {
            new TestItem { Id = Guid.NewGuid(), Name = "Item 1" },
            new TestItem { Id = Guid.NewGuid(), Name = "Item 2" }
        };
        await _service.InsertItemsAsync(items);

        var result = await _service.ReadItemsAsync<TestItem>();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Id == items[0].Id);
        Assert.Contains(result, x => x.Id == items[1].Id);
    }

    [Fact]
    public async Task FindItemsAsync_ReturnsMatchingItems()
    {
        var items = new List<TestItem>
        {
            new TestItem { Id = Guid.NewGuid(), Name = "Target" },
            new TestItem { Id = Guid.NewGuid(), Name = "Other" }
        };
        await _service.InsertItemsAsync(items);

        var result = await _service.FindItemsAsync<TestItem>(x => x.Name == "Target");

        Assert.Single(result);
        Assert.Equal("Target", result.First().Name);
    }

    [Fact]
    public async Task DatabaseMetadata_PersistsNameAndPath()
    {
        var dbMeta = new DatabaseMetadata
        {
            Name = "My Custom Db",
            Path = "/path/to/db.vvo"
        };

        await _service.InsertItemsAsync(new[] { dbMeta });
        var loaded = await _service.ReadItemsAsync<DatabaseMetadata>();

        var item = Assert.Single(loaded);
        Assert.Equal("My Custom Db", item.Name);
        Assert.Equal("/path/to/db.vvo", item.Path);
    }

    [Fact]
    public async Task WritingOperations_RaiseModified()
    {
        var item = new TestItem { Id = Guid.NewGuid(), Name = "Item 1" };
        var raised = 0;
        _service.Modified += (_, _) => Interlocked.Increment(ref raised);

        await _service.InsertItemsAsync(new[] { item });
        await _service.UpdateItemsAsync(new[] { item });
        await _service.RemoveItemsAsync<TestItem>(new[] { item.Id });

        Assert.Equal(3, raised);
    }

    [Fact]
    public async Task ReadingOperations_DoNotRaiseModified()
    {
        await _service.InsertItemsAsync(new[] { new TestItem { Id = Guid.NewGuid(), Name = "Item 1" } });

        var raised = 0;
        _service.Modified += (_, _) => Interlocked.Increment(ref raised);

        await _service.ReadItemsAsync<TestItem>();
        await _service.FindItemsAsync<TestItem>(x => x.Name == "Item 1");

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Modified_IsRaisedOnlyOnceTheFileIsClosed()
    {
        // LiteDB checkpoints when the database is disposed, so a handler that runs
        // any earlier would be looking at a file the write has not reached yet.
        long sizeAtRaise = 0;
        _service.Modified += (_, _) => sizeAtRaise = new FileInfo(_dbPath).Length;

        var bulk = Enumerable.Range(0, 500)
            .Select(i => new TestItem { Id = Guid.NewGuid(), Name = $"Item {i}" })
            .ToList();

        var sizeBefore = new FileInfo(_dbPath).Length;

        await _service.InsertItemsAsync(bulk);

        Assert.True(sizeAtRaise > sizeBefore,
            $"file was {sizeAtRaise} bytes when Modified fired, {sizeBefore} before the insert");
    }

    [Fact]
    public async Task VirtualVolumeRecord_PersistsItsName()
    {
        var record = new VirtualVolumeRecord
        {
            Id = Guid.NewGuid(),
            Name = "Backup Drives",
            Icon = "CompactDisc",
            Color = "#FF3366"
        };

        await _service.InsertItemsAsync(new[] { record });
        var loaded = (await _service.ReadItemsAsync<VirtualVolumeRecord>()).Single();

        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task VirtualVolumeRecord_PersistsARename()
    {
        var record = new VirtualVolumeRecord
        {
            Id = Guid.NewGuid(),
            Name = "Archives",
            Icon = "HardDrive"
        };
        await _service.InsertItemsAsync(new[] { record });

        await _service.UpdateItemsAsync(new[] { record with { Name = "Old Archives" } });

        var loaded = (await _service.ReadItemsAsync<VirtualVolumeRecord>()).Single();
        Assert.Equal("Old Archives", loaded.Name);
    }

    [Fact]
    public async Task FileRecord_PersistsTimestamps()
    {
        var rootId = Guid.NewGuid();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootId,
            ParentId = rootId,
            IsFolder = false,
            Name = "file.txt",
            Size = 1234,
            Created = new DateTime(2026, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc),
            Modified = new DateTime(2026, 8, 9, 10, 11, 12, 345, DateTimeKind.Utc)
        };

        await _service.InsertItemsAsync(new[] { record });
        var loaded = (await _service.ReadItemsAsync<FileRecord>()).Single();

        Assert.Equal(record.Created, loaded.Created);
        Assert.Equal(record.Modified, loaded.Modified);
        Assert.Equal(DateTimeKind.Utc, loaded.Created.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.Modified.Kind);
        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task RootFolderMetadata_PersistsDescriptionAndLabel()
    {
        var id = Guid.NewGuid();
        var rootMeta = new RootFolderMetadata
        {
            Id = id,
            VirtualVolumeId = Guid.NewGuid(),
            TreeId = Guid.NewGuid(),
            Path = "/path/to/folder",
            LastScanned = DateTime.UtcNow,
            Label = "Photos Drive",
            Description = "Main storage for holiday photos"
        };

        await _service.InsertItemsAsync(new[] { rootMeta });
        var loaded = await _service.ReadItemsAsync<RootFolderMetadata>();

        var item = Assert.Single(loaded);
        Assert.Equal(id, item.Id);
        Assert.Equal(rootMeta.VirtualVolumeId, item.VirtualVolumeId);
        Assert.Equal(rootMeta.TreeId, item.TreeId);
        Assert.Equal("/path/to/folder", item.Path);
        Assert.Equal("Photos Drive", item.Label);
        Assert.Equal("Main storage for holiday photos", item.Description);
    }

    [Fact]
    public async Task ShrinkDatabase_KeepsEverythingThatWasStored()
    {
        await _service.InsertItemsAsync<DatabaseMetadata>(
            [new DatabaseMetadata { Id = Guid.NewGuid(), Name = "Discs", Path = _dbPath }]);

        await _service.ShrinkDatabaseAsync();

        Assert.Equal("Discs", (await _service.ReadItemsAsync<DatabaseMetadata>()).Single().Name);
    }

    [Fact]
    public async Task ShrinkDatabase_ReclaimsWhatDeletedRecordsWereHolding()
    {
        var records = Enumerable.Range(0, 2000).Select(i => new FileRecord
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            Name = $"file{i}.txt",
            Size = i
        }).ToList();

        await _service.InsertItemsAsync(records);
        await _service.RemoveItemsAsync<FileRecord>(record => record.Size >= 0);

        var before = new FileInfo(_dbPath).Length;
        await _service.ShrinkDatabaseAsync();

        Assert.True(new FileInfo(_dbPath).Length < before);
    }

    /// <summary>
    /// A tree of the size a real scan produces. The smaller sets the other tests use sit under
    /// the point where rebuilding a secondary index goes wrong, which is how a shrink that
    /// failed on every real catalogue went unnoticed.
    /// </summary>
    private async Task GivenACatalogueOfAsync(int count)
    {
        var rootId = Guid.NewGuid();

        await _service.InsertItemsAsync(Enumerable.Range(0, count).Select(i => new FileRecord
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootId,
            ParentId = rootId,
            Name = $"file-{i}-named-about-as-long-as-these-usually-are.txt",
            Size = i
        }).ToList());
    }

    [Fact]
    public async Task ShrinkDatabase_SurvivesACatalogueOfARealisticSize()
    {
        await GivenACatalogueOfAsync(10_000);

        await _service.ShrinkDatabaseAsync();

        Assert.Equal(10_000, (await _service.ReadItemsAsync<FileRecord>()).Count);
    }

    // Dropped only to get the rebuild past them, so the database has to come back indexed
    [Fact]
    public async Task ShrinkDatabase_PutsTheIndexesBack()
    {
        await GivenACatalogueOfAsync(10_000);

        await _service.ShrinkDatabaseAsync();

        using var db = new LiteDatabase(_dbPath);
        var indexes = db
            .Execute("SELECT $.collection, $.name FROM $indexes")
            .ToEnumerable()
            .Select(index => $"{index["collection"].AsString}.{index["name"].AsString}")
            .ToList();

        Assert.Contains("FileRecord.Name", indexes);
        Assert.Contains("FileRecord.RootFolderId", indexes);
        Assert.Contains("FileRecord.ParentId", indexes);
        Assert.Contains("RootFolderMetadata.TreeId", indexes);
    }

    [Fact]
    public async Task ShrinkDatabase_LeavesTheRecordsQueryableByAnIndexedField()
    {
        await GivenACatalogueOfAsync(10_000);

        await _service.ShrinkDatabaseAsync();

        var found = await _service.FindItemsAsync<FileRecord>(
            record => record.Name == "file-9999-named-about-as-long-as-these-usually-are.txt");

        Assert.Equal(9999, found.Single().Size);
    }

    [Fact]
    public async Task ShrinkDatabase_ReportsEachStepItGoesThrough()
    {
        await GivenACatalogueOfAsync(2_000);
        var reported = new List<string>();

        await _service.ShrinkDatabaseAsync(new Progress<string>(reported.Add));
        await Task.Yield();

        Assert.Contains(reported, message => message.StartsWith("Removing indexes..."));
        Assert.Contains(reported, message => message.StartsWith("Compacting"));
        Assert.Contains(reported, message => message.Contains("Rebuilding indexes... 8 of 8"));
    }

    [Fact]
    public async Task ShrinkDatabase_StopsWhenCancelled()
    {
        await GivenACatalogueOfAsync(2_000);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ShrinkDatabaseAsync(null, cancellation.Token));
    }

    // Stopping costs the indexes at worst, and the next open builds those again
    [Fact]
    public async Task ShrinkDatabase_KeepsEveryRecordWhenCancelled()
    {
        await GivenACatalogueOfAsync(2_000);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ShrinkDatabaseAsync(null, cancellation.Token));

        Assert.Equal(2_000, (await _service.ReadItemsAsync<FileRecord>()).Count);

        await _service.EnsureDatabaseReadyAsync(_dbPath);

        using var db = new LiteDatabase(_dbPath);
        var indexes = db
            .Execute("SELECT $.collection, $.name FROM $indexes")
            .ToEnumerable()
            .Select(index => $"{index["collection"].AsString}.{index["name"].AsString}")
            .ToList();

        Assert.Contains("FileRecord.Name", indexes);
    }

    // LiteDB renames the old file aside instead of overwriting it, and leaves it there
    [Fact]
    public async Task ShrinkDatabase_LeavesTheOldFileBesideTheNewOne()
    {
        Assert.Null(_service.ShrinkBackupPath());

        await GivenACatalogueOfAsync(2_000);
        await _service.ShrinkDatabaseAsync();

        var backup = _service.ShrinkBackupPath();
        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));

        try
        {
            Assert.EndsWith("-backup.vvo", backup);
        }
        finally
        {
            File.Delete(backup!);
        }
    }

    [Fact]
    public async Task ShrinkDatabase_AnnouncesTheWrite()
    {
        var announced = 0;
        _service.Modified += (_, _) => Interlocked.Increment(ref announced);

        await _service.ShrinkDatabaseAsync();

        Assert.Equal(1, announced);
    }

}