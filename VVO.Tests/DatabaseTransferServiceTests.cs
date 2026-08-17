using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Tests;

public class DatabaseTransferServiceTests : IDisposable
{
    private readonly List<string> _paths = [];
    private readonly string _dbPath;
    private readonly string _jsonPath;

    private readonly DatabaseService _database;
    private readonly VirtualVolumeService _volumes;
    private readonly DatabaseTransferService _transfer;

    public DatabaseTransferServiceTests()
    {
        _dbPath = TempPath("vvo");
        _jsonPath = TempPath("json");

        _database = new DatabaseService();
        _database.EnsureDatabaseReadyAsync(_dbPath).GetAwaiter().GetResult();
        _volumes = new VirtualVolumeService(_database);
        _transfer = new DatabaseTransferService(_database);
    }

    private string TempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.{extension}");
        _paths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _paths.Where(File.Exists))
        {
            try { File.Delete(path); } catch { }
        }
    }

    private async Task<RootFolderMetadata> AddFolderAsync(Guid volumeId, string name = "root")
    {
        var tree = TestTree.Root(name).File("a.txt", 10).Build();
        return await _volumes.AddFolderAsync(volumeId, tree.Metadata, tree.Records.ToList());
    }

    [Fact]
    public async Task Export_ThenImport_CarriesTheCatalogueOver()
    {
        await _database.InsertItemsAsync([new DatabaseMetadata { Name = "Discs", Path = _dbPath }]);
        var volume = await _volumes.CreateVirtualVolumeAsync("Backups", "HardDrive", "#FF3366");
        var folder = await AddFolderAsync(volume.Id);
        var files = await _database.FindItemsAsync<FileRecord>(record => record.RootFolderId == folder.TreeId);

        await _transfer.ExportAsync(_jsonPath);

        var target = TempPath("vvo");
        await _transfer.ImportAsync(_jsonPath, target);

        Assert.Equal(target, _database.DbPath);
        Assert.Equal("Discs", (await _database.ReadItemsAsync<DatabaseMetadata>()).Single().Name);
        Assert.Equal(volume, Assert.Single(await _volumes.GetVirtualVolumesAsync()));
        Assert.Equal(folder, Assert.Single(await _volumes.GetFoldersAsync(volume.Id)));
        Assert.Equal(
            files.OrderBy(record => record.Name).ToList(),
            (await _database.ReadItemsAsync<FileRecord>()).OrderBy(record => record.Name).ToList());
    }

    [Fact]
    public async Task Export_LeavesOutTreesNoFolderRefersTo()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("Backups", "HardDrive");
        var kept = await AddFolderAsync(volume.Id, "kept");
        var removed = await AddFolderAsync(volume.Id, "removed");
        await _volumes.RemoveFolderAsync(removed.Id);

        await _transfer.ExportAsync(_jsonPath);

        var target = TempPath("vvo");
        await _transfer.ImportAsync(_jsonPath, target);

        var records = await _database.ReadItemsAsync<FileRecord>();
        Assert.All(records, record => Assert.Equal(kept.TreeId, record.RootFolderId));
    }

    [Fact]
    public async Task Import_ReplacesWhateverIsAtTheTargetPath()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("Backups", "HardDrive");
        await _transfer.ExportAsync(_jsonPath);

        var target = TempPath("vvo");
        await _database.EnsureDatabaseReadyAsync(target);
        await _volumes.CreateVirtualVolumeAsync("Something Else", "CompactDisc");

        await _transfer.ImportAsync(_jsonPath, target);

        Assert.Equal(volume, Assert.Single(await _volumes.GetVirtualVolumesAsync()));
    }

    [Fact]
    public async Task Import_RejectsAFormatFromTheFuture()
    {
        await File.WriteAllTextAsync(_jsonPath, """{"formatVersion":99}""");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _transfer.ImportAsync(_jsonPath, TempPath("vvo")));
    }

    [Fact]
    public async Task Import_RejectsAFileHoldingNoExport()
    {
        await File.WriteAllTextAsync(_jsonPath, "null");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _transfer.ImportAsync(_jsonPath, TempPath("vvo")));
    }

    [Fact]
    public async Task Import_RejectsAFileThatIsNotJson()
    {
        await File.WriteAllTextAsync(_jsonPath, "this is not json");

        await Assert.ThrowsAnyAsync<Exception>(
            () => _transfer.ImportAsync(_jsonPath, TempPath("vvo")));
    }

    [Fact]
    public async Task Export_OfAnEmptyDatabaseImportsBackAsEmpty()
    {
        await _transfer.ExportAsync(_jsonPath);

        var target = TempPath("vvo");
        await _transfer.ImportAsync(_jsonPath, target);

        Assert.Empty(await _volumes.GetVirtualVolumesAsync());
        Assert.Empty(await _database.ReadItemsAsync<FileRecord>());
    }

    [Fact]
    public async Task BothWaysReportTheirProgress()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("Backups", "HardDrive");
        await AddFolderAsync(volume.Id);

        var exporting = new List<string>();
        await _transfer.ExportAsync(_jsonPath, new Progress<string>(exporting.Add));

        var importing = new List<string>();
        await _transfer.ImportAsync(_jsonPath, TempPath("vvo"), new Progress<string>(importing.Add));

        // Progress arrives on the captured context, so give the posted callbacks a turn
        await Task.Yield();
        Assert.NotEmpty(exporting);
        Assert.NotEmpty(importing);
    }

    [Fact]
    public async Task AnExportCarriesTheFormatItWasWrittenIn()
    {
        await _transfer.ExportAsync(_jsonPath);

        var json = await File.ReadAllTextAsync(_jsonPath);

        Assert.Contains($"\"formatVersion\":{DatabaseTransferService.CurrentFormatVersion}", json);
    }
}
