using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Tests;

public class VirtualVolumeServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _database;
    private readonly VirtualVolumeService _service;

    public VirtualVolumeServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
        _database = new DatabaseService();
        _database.EnsureDatabaseReadyAsync(_dbPath).GetAwaiter().GetResult();
        _service = new VirtualVolumeService(_database);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private async Task<RootFolderMetadata> AddFolderAsync(Guid volumeId, string name = "root")
    {
        var tree = TestTree.Root(name).File("a.txt", 10).Build();
        return await _service.AddFolderAsync(volumeId, tree.Metadata, tree.Records.ToList());
    }

    private Task<IReadOnlyCollection<FileRecord>> FilesOfAsync(Guid treeId)
    {
        return _database.FindItemsAsync<FileRecord>(record => record.RootFolderId == treeId);
    }

    #region Virtual volumes

    [Fact]
    public async Task NewDatabase_HasNoVirtualVolumes()
    {
        Assert.Empty(await _service.GetVirtualVolumesAsync());
    }

    [Fact]
    public async Task CreateVirtualVolume_StoresIt()
    {
        var created = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");

        var loaded = Assert.Single(await _service.GetVirtualVolumesAsync());
        Assert.Equal(created, loaded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateVirtualVolume_RejectsABlankName(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateVirtualVolumeAsync(name, "HardDrive"));
    }

    [Fact]
    public async Task CreateVirtualVolume_StoresItsAppearance()
    {
        var created = await _service.CreateVirtualVolumeAsync("Backups", "CompactDisc", "#FF3366");

        var loaded = Assert.Single(await _service.GetVirtualVolumesAsync());
        Assert.Equal("CompactDisc", loaded.Icon);
        Assert.Equal("#FF3366", loaded.Color);
        Assert.Equal(created, loaded);
    }

    [Fact]
    public async Task CreateVirtualVolume_RejectsABlankIcon()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateVirtualVolumeAsync("Backups", "  "));
    }

    [Fact]
    public async Task UpdateVirtualVolume_KeepsTheIdentity()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive", "#FF3366");

        await _service.UpdateVirtualVolumeAsync(volume.Id, "Old Backups", "FloppyDisk", null);

        var loaded = Assert.Single(await _service.GetVirtualVolumesAsync());
        Assert.Equal(volume.Id, loaded.Id);
        Assert.Equal("Old Backups", loaded.Name);
        Assert.Equal("FloppyDisk", loaded.Icon);
        Assert.Null(loaded.Color);
    }

    [Fact]
    public async Task UpdateVirtualVolume_LeavesTheFoldersAlone()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);

        await _service.UpdateVirtualVolumeAsync(volume.Id, "Old Backups", "FloppyDisk", null);

        Assert.Equal(folder, Assert.Single(await _service.GetFoldersAsync(volume.Id)));
    }

    [Fact]
    public async Task UpdateVirtualVolume_RejectsAnUnknownVolume()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateVirtualVolumeAsync(Guid.NewGuid(), "Backups", "HardDrive", null));
    }

    [Fact]
    public async Task DeleteVirtualVolume_TakesItsFoldersWithIt()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var kept = await _service.CreateVirtualVolumeAsync("Archives", "HardDrive");
        await AddFolderAsync(volume.Id);
        var survivor = await AddFolderAsync(kept.Id);

        await _service.DeleteVirtualVolumeAsync(volume.Id);

        Assert.Empty(await _service.GetFoldersAsync(volume.Id));
        Assert.Equal(survivor, Assert.Single(await _service.GetFoldersAsync(kept.Id)));
        Assert.Equal(kept, Assert.Single(await _service.GetVirtualVolumesAsync()));
    }

    [Fact]
    public async Task DeleteVirtualVolume_DropsTheFoldersItHeld()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        await AddFolderAsync(volume.Id);

        await _service.DeleteVirtualVolumeAsync(volume.Id);

        Assert.Empty(await _service.GetFoldersAsync(volume.Id));
    }

    [Fact]
    public async Task DeleteVirtualVolume_RejectsAnUnknownVolume()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteVirtualVolumeAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeletingTheLastVirtualVolume_LeavesAUsableDatabase()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        await _service.DeleteVirtualVolumeAsync(volume.Id);

        var recreated = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");

        Assert.Equal(recreated, Assert.Single(await _service.GetVirtualVolumesAsync()));
    }

    #endregion

    #region Adding folders

    [Fact]
    public async Task AddFolder_StoresTheTreeAndPlacesTheEntry()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var tree = TestTree.Root("photos").File("a.txt", 10).Folder("sub", f => f.File("b.txt", 20)).Build();

        var entry = await _service.AddFolderAsync(volume.Id, tree.Metadata, tree.Records.ToList());

        Assert.Equal(volume.Id, entry.VirtualVolumeId);
        Assert.Equal(tree.Metadata.TreeId, entry.TreeId);
        Assert.Equal(entry, Assert.Single(await _service.GetFoldersAsync(volume.Id)));
        Assert.Equal(tree.Records.Count, (await FilesOfAsync(entry.TreeId)).Count);
    }

    [Fact]
    public async Task AddFolder_RejectsAnUnknownVolume()
    {
        var tree = TestTree.Root().Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AddFolderAsync(Guid.NewGuid(), tree.Metadata, tree.Records.ToList()));
    }

    [Fact]
    public async Task AddFolder_RejectsRecordsWithoutTheirRoot()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var tree = TestTree.Root().File("a.txt", 10).Build();
        var withoutRoot = tree.Records.Where(record => record.Id != tree.Metadata.TreeId).ToList();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AddFolderAsync(volume.Id, tree.Metadata, withoutRoot));
    }

    [Fact]
    public async Task AddFolder_WritesNothingWhenTheVolumeIsUnknown()
    {
        var tree = TestTree.Root().Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AddFolderAsync(Guid.NewGuid(), tree.Metadata, tree.Records.ToList()));

        Assert.Empty(await FilesOfAsync(tree.Metadata.TreeId));
    }

    [Fact]
    public async Task UpdateFolder_StoresHowTheFolderIsPresented()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);

        var updated = await _service.UpdateFolderAsync(
            folder.Id, "Holiday photos", "Summer 2026", "StarFilled", "#FF3366");

        Assert.Equal("Holiday photos", updated.Label);
        Assert.Equal("Summer 2026", updated.Description);
        Assert.Equal("StarFilled", updated.Icon);
        Assert.Equal("#FF3366", updated.Color);
        Assert.Equal(updated, Assert.Single(await _service.GetFoldersAsync(volume.Id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task UpdateFolder_StoresABlankFieldAsAbsent(string? blank)
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        await _service.UpdateFolderAsync(folder.Id, "Holiday photos", "Summer 2026", "StarFilled", "#FF3366");

        var updated = await _service.UpdateFolderAsync(folder.Id, blank, blank, blank, blank);

        Assert.Null(updated.Label);
        Assert.Null(updated.Description);
        Assert.Null(updated.Icon);
        Assert.Null(updated.Color);
    }

    [Fact]
    public async Task UpdateFolder_LeavesTheTreeAndItsPlacementAlone()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var storedRecords = (await FilesOfAsync(folder.TreeId)).Count;

        var updated = await _service.UpdateFolderAsync(folder.Id, "Renamed", null, null, null);

        Assert.Equal(folder.TreeId, updated.TreeId);
        Assert.Equal(folder.VirtualVolumeId, updated.VirtualVolumeId);
        Assert.Equal(storedRecords, (await FilesOfAsync(folder.TreeId)).Count);
    }

    [Fact]
    public async Task UpdateFolder_RejectsAnUnknownEntry()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateFolderAsync(Guid.NewGuid(), "Renamed", null, null, null));
    }

    #endregion

    #region Copy, duplicate and move

    [Fact]
    public async Task CopyFolder_SharesTheTreeWithTheOriginal()
    {
        var source = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var target = await _service.CreateVirtualVolumeAsync("Archives", "HardDrive");
        var original = await AddFolderAsync(source.Id);
        var storedRecords = (await FilesOfAsync(original.TreeId)).Count;

        var copy = await _service.CopyFolderAsync(original.Id, target.Id);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.TreeId, copy.TreeId);
        Assert.Equal(target.Id, copy.VirtualVolumeId);
        Assert.Equal(original, Assert.Single(await _service.GetFoldersAsync(source.Id)));
        Assert.Equal(copy, Assert.Single(await _service.GetFoldersAsync(target.Id)));
        Assert.Equal(storedRecords, (await FilesOfAsync(original.TreeId)).Count);
    }

    [Fact]
    public async Task CopyFolder_IntoItsOwnVolumeDuplicatesIt()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var original = await AddFolderAsync(volume.Id);

        var duplicate = await _service.CopyFolderAsync(original.Id, volume.Id, "Second pass");

        var folders = await _service.GetFoldersAsync(volume.Id);
        Assert.Equal(2, folders.Count);
        Assert.Equal("Second pass", duplicate.Label);
        Assert.Equal(original.TreeId, duplicate.TreeId);
    }

    [Fact]
    public async Task CopyFolder_KeepsTheOriginalLabelWhenNoneIsGiven()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var tree = TestTree.Root().Build();
        var original = await _service.AddFolderAsync(
            volume.Id, tree.Metadata with { Label = "Holiday photos" }, tree.Records.ToList());

        var copy = await _service.CopyFolderAsync(original.Id, volume.Id);

        Assert.Equal("Holiday photos", copy.Label);
    }

    [Fact]
    public async Task CopyFolder_RejectsAnUnknownEntryOrTarget()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var original = await AddFolderAsync(volume.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CopyFolderAsync(Guid.NewGuid(), volume.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CopyFolderAsync(original.Id, Guid.NewGuid()));
        Assert.Single(await _service.GetFoldersAsync(volume.Id));
    }

    [Fact]
    public async Task MoveFolder_LeavesTheSourceVolumeEmpty()
    {
        var source = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var target = await _service.CreateVirtualVolumeAsync("Archives", "HardDrive");
        var folder = await AddFolderAsync(source.Id);
        var storedRecords = (await FilesOfAsync(folder.TreeId)).Count;

        await _service.MoveFolderAsync(folder.Id, target.Id);

        Assert.Empty(await _service.GetFoldersAsync(source.Id));
        var moved = Assert.Single(await _service.GetFoldersAsync(target.Id));
        Assert.Equal(folder.Id, moved.Id);
        Assert.Equal(folder.TreeId, moved.TreeId);
        Assert.Equal(storedRecords, (await FilesOfAsync(folder.TreeId)).Count);
    }

    [Fact]
    public async Task MoveFolder_RejectsAnUnknownEntryOrTarget()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.MoveFolderAsync(Guid.NewGuid(), volume.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MoveFolderAsync(folder.Id, Guid.NewGuid()));
    }

    #endregion

    #region Removal

    [Fact]
    public async Task RemoveFolder_DropsTheEntry()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);

        await _service.RemoveFolderAsync(folder.Id);

        Assert.Empty(await _service.GetFoldersAsync(volume.Id));
    }

    [Fact]
    public async Task RemoveFolder_RejectsAnUnknownEntry()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RemoveFolderAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RestoreVirtualVolume_PutsAnEmptyVolumeBackAsItWas()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive", "#FF3366");
        await _service.DeleteVirtualVolumeAsync(volume.Id);

        await _service.RestoreVirtualVolumeAsync(volume);

        Assert.Equal(volume, Assert.Single(await _service.GetVirtualVolumesAsync()));
    }

    [Fact]
    public async Task RestoreVirtualVolume_RejectsOneThatIsStillThere()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RestoreVirtualVolumeAsync(volume));
    }

    [Fact]
    public async Task RestoreFolder_PutsACopyBackAsItWas()
    {
        var source = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var target = await _service.CreateVirtualVolumeAsync("Archives", "HardDrive");
        var original = await AddFolderAsync(source.Id);
        var copy = await _service.CopyFolderAsync(original.Id, target.Id);

        await _service.RemoveFolderAsync(copy.Id);
        await _service.RestoreFolderAsync(copy);

        Assert.Equal(copy, Assert.Single(await _service.GetFoldersAsync(target.Id)));
    }

    [Fact]
    public async Task RestoreFolder_RejectsOneThatIsStillThere()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RestoreFolderAsync(folder));
    }

    // A delete takes the tree with it, which is what makes a delete final
    [Fact]
    public async Task RestoreFolder_RejectsAFolderWhoseTreeWentWithIt()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);

        await _service.RemoveFolderAsync(folder.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RestoreFolderAsync(folder));
    }

    [Fact]
    public async Task RemoveFolder_TakesTheTreeWithIt()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var kept = await AddFolderAsync(volume.Id, "kept");
        var removed = await AddFolderAsync(volume.Id, "removed");

        await _service.RemoveFolderAsync(removed.Id);

        Assert.NotEmpty(await FilesOfAsync(kept.TreeId));
        Assert.Empty(await FilesOfAsync(removed.TreeId));
    }

    [Fact]
    public async Task RemoveFolder_KeepsATreeAnotherEntryStillShares()
    {
        var source = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var target = await _service.CreateVirtualVolumeAsync("Archives", "HardDrive");
        var original = await AddFolderAsync(source.Id);
        await _service.CopyFolderAsync(original.Id, target.Id);

        await _service.RemoveFolderAsync(original.Id);

        Assert.NotEmpty(await FilesOfAsync(original.TreeId));
    }

    [Fact]
    public async Task DeleteVirtualVolume_TakesTheTreesOfItsFoldersWithIt()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);

        await _service.DeleteVirtualVolumeAsync(volume.Id);

        Assert.Empty(await FilesOfAsync(folder.TreeId));
    }

    [Fact]
    public async Task DeleteVirtualVolume_KeepsATreeAFolderElsewhereStillShares()
    {
        var source = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var target = await _service.CreateVirtualVolumeAsync("Archives", "HardDrive");
        var original = await AddFolderAsync(source.Id);
        await _service.CopyFolderAsync(original.Id, target.Id);

        await _service.DeleteVirtualVolumeAsync(source.Id);

        Assert.NotEmpty(await FilesOfAsync(original.TreeId));
    }

    #endregion

    #region Deleting

    // Counted per tree: a tree goes in one statement that cannot report from within
    [Fact]
    public async Task DeletingAVolumeReportsTheTreesItCollects()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        await AddFolderAsync(volume.Id, "one");
        await AddFolderAsync(volume.Id, "two");
        var reported = new List<string>();

        await _service.DeleteVirtualVolumeAsync(volume.Id, new Progress<string>(reported.Add));
        await Task.Yield();

        Assert.Contains(reported, message => message.Contains("2 of 2"));
    }

    [Fact]
    public async Task CancellingADeleteLeavesTheVolumeAndItsFilesWhereTheyWere()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var entry = await AddFolderAsync(volume.Id);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.DeleteVirtualVolumeAsync(volume.Id, null, cancellation.Token));

        Assert.Single(await _service.GetVirtualVolumesAsync());
        Assert.NotEmpty(await FilesOfAsync(entry.TreeId));
    }

    [Fact]
    public async Task CancellingAFolderRemovalLeavesTheFolderWhereItWas()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var entry = await AddFolderAsync(volume.Id);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.RemoveFolderAsync(entry.Id, null, cancellation.Token));

        Assert.Single(await _service.GetFoldersAsync(volume.Id));
        Assert.NotEmpty(await FilesOfAsync(entry.TreeId));
    }

    // A folder still shared with another volume collects no tree, so there is nothing to report
    [Fact]
    public async Task RemovingAFolderThatSharesItsTreeCollectsNothing()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var other = await _service.CreateVirtualVolumeAsync("Archives", "HardDrive");
        var entry = await AddFolderAsync(volume.Id);
        await _service.CopyFolderAsync(entry.Id, other.Id);
        var reported = new List<string>();

        await _service.RemoveFolderAsync(entry.Id, new Progress<string>(reported.Add));
        await Task.Yield();

        Assert.DoesNotContain(reported, message => message.StartsWith("Deleting files"));
        Assert.NotEmpty(await FilesOfAsync(entry.TreeId));
    }

    #endregion

    #region Saving a scanned tree

    // Unlike a scan, the total is known here, so the save can say how far along it is
    [Fact]
    public async Task SavingAFolderReportsHowFarThroughTheRecordsItIs()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var tree = TestTree.Root("root").File("a.txt", 10).File("b.txt", 20).Build();
        var reported = new List<string>();

        await _service.AddFolderAsync(
            volume.Id, tree.Metadata, tree.Records.ToList(), new Progress<string>(reported.Add));

        await Task.Yield();
        Assert.Contains(reported, message => message.Contains("3 of 3"));
    }

    // The save is one transaction, so a cancelled one leaves the database as it found it
    [Fact]
    public async Task CancellingASaveLeavesNothingBehind()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var tree = TestTree.Root("root").File("a.txt", 10).Build();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.AddFolderAsync(
            volume.Id, tree.Metadata, tree.Records.ToList(), null, cancellation.Token));

        Assert.Empty(await FilesOfAsync(tree.Metadata.TreeId));
        Assert.Empty(await _service.GetFoldersAsync(volume.Id));
    }

    [Fact]
    public async Task ASaveThatIsNotCancelledStillStoresEverything()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var tree = TestTree.Root("root").File("a.txt", 10).File("b.txt", 20).Build();

        using var cancellation = new CancellationTokenSource();
        await _service.AddFolderAsync(
            volume.Id, tree.Metadata, tree.Records.ToList(), null, cancellation.Token);

        Assert.Equal(3, (await FilesOfAsync(tree.Metadata.TreeId)).Count);
    }

    #endregion

    #region Updating what a folder holds

    // The folder is read again from wherever it is now, which is a tree of its own with ids
    // of its own; what it replaces is the tree the catalogue already had
    private static FolderTree Rescanned(string name = "root", string path = @"D:\root")
    {
        return TestTree.Root(name, path).File("b.txt", 20).File("c.txt", 30).Build();
    }

    [Fact]
    public async Task UpdateFolderContents_ReplacesWhatWasCatalogued()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var rescanned = Rescanned();

        await _service.UpdateFolderContentsAsync(folder.Id, rescanned.Metadata, rescanned.Records.ToList());

        var stored = await FilesOfAsync(folder.TreeId);
        Assert.Equal(["b.txt", "c.txt", "root"], stored.Select(record => record.Name).Order());
    }

    // A copy or a duplicate stands on the tree it was made from, so it has to come away holding
    // what the update put there rather than pointing at records that have just been deleted
    [Fact]
    public async Task UpdateFolderContents_KeepsTheTreeSoEveryEntryOnItFollows()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var copy = await _service.CopyFolderAsync(folder.Id, volume.Id, "A copy");
        var rescanned = Rescanned();

        var updated = await _service.UpdateFolderContentsAsync(
            folder.Id, rescanned.Metadata, rescanned.Records.ToList());

        Assert.Equal(folder.TreeId, updated.TreeId);
        Assert.Equal(folder.TreeId, copy.TreeId);
        Assert.Equal(3, (await FilesOfAsync(copy.TreeId)).Count);
    }

    [Fact]
    public async Task UpdateFolderContents_LeavesTheRerootedTreeReachableFromItsRoot()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var rescanned = TestTree.Root("root").Folder("sub", f => f.File("b.txt", 20)).Build();

        await _service.UpdateFolderContentsAsync(folder.Id, rescanned.Metadata, rescanned.Records.ToList());

        var stored = await FilesOfAsync(folder.TreeId);
        var root = Assert.Single(stored, record => record.ParentId == null);
        var sub = stored.Single(record => record.Name == "sub");

        Assert.Equal(folder.TreeId, root.Id);
        Assert.Equal(folder.TreeId, sub.ParentId);
        Assert.All(stored, record => Assert.Equal(folder.TreeId, record.RootFolderId));
    }

    [Fact]
    public async Task UpdateFolderContents_RecordsWhereAndWhenTheFolderWasRead()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var rescanned = Rescanned(path: @"E:\root");

        var updated = await _service.UpdateFolderContentsAsync(
            folder.Id, rescanned.Metadata, rescanned.Records.ToList());

        Assert.Equal(@"E:\root", updated.Path);
        Assert.Equal(rescanned.Metadata.LastScanned, updated.LastScanned);
        Assert.Equal(updated, Assert.Single(await _service.GetFoldersAsync(volume.Id)));
    }

    [Fact]
    public async Task UpdateFolderContents_LeavesHowTheFolderIsPresentedAlone()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        await _service.UpdateFolderAsync(folder.Id, "Holiday photos", "Summer 2026", "StarFilled", "#FF3366");
        var rescanned = Rescanned();

        var updated = await _service.UpdateFolderContentsAsync(
            folder.Id, rescanned.Metadata, rescanned.Records.ToList());

        Assert.Equal("Holiday photos", updated.Label);
        Assert.Equal("Summer 2026", updated.Description);
        Assert.Equal("StarFilled", updated.Icon);
        Assert.Equal("#FF3366", updated.Color);
        Assert.Equal(volume.Id, updated.VirtualVolumeId);
    }

    [Fact]
    public async Task UpdateFolderContents_RejectsAnUnknownEntry()
    {
        var rescanned = Rescanned();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateFolderContentsAsync(
            Guid.NewGuid(), rescanned.Metadata, rescanned.Records.ToList()));
    }

    [Fact]
    public async Task UpdateFolderContents_RejectsRecordsWithoutTheirRoot()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var rescanned = Rescanned();
        var withoutRoot = rescanned.Records
            .Where(record => record.Id != rescanned.Metadata.TreeId)
            .ToList();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateFolderContentsAsync(folder.Id, rescanned.Metadata, withoutRoot));
    }

    // One transaction, so the folder is either read afresh or left holding exactly what it had
    [Fact]
    public async Task CancellingAnUpdateLeavesWhatWasCatalogued()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var rescanned = Rescanned(path: @"E:\root");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.UpdateFolderContentsAsync(
            folder.Id, rescanned.Metadata, rescanned.Records.ToList(), null, cancellation.Token));

        var stored = await FilesOfAsync(folder.TreeId);
        Assert.Equal(["a.txt", "root"], stored.Select(record => record.Name).Order());
        Assert.Equal(folder, Assert.Single(await _service.GetFoldersAsync(volume.Id)));
    }

    [Fact]
    public async Task UpdateFolderContents_ReportsWhatItIsDoingAndHowFarThroughItIs()
    {
        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        var rescanned = Rescanned();
        var reported = new List<string>();

        await _service.UpdateFolderContentsAsync(
            folder.Id, rescanned.Metadata, rescanned.Records.ToList(), new Progress<string>(reported.Add));

        await Task.Yield();
        Assert.Contains(reported, message => message.Contains("Removing"));
        Assert.Contains(reported, message => message.Contains("3 of 3"));
    }

    #endregion
}
