using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Tests;

public class VolumeComparisonIntegrationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;

    private readonly FileScannerService _scanner = new();
    private readonly DatabaseService _database = new();
    private readonly FolderCompareService _comparer = new();

    public VolumeComparisonIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"VVO_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
        _database.EnsureDatabaseReadyAsync(_dbPath).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }

        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private void CreateFile(string relativePath, int sizeBytes)
    {
        var fullPath = Path.Combine(_testRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);
    }

    [Fact]
    public async Task ScannedVolume_MatchesItselfAfterADatabaseRoundTrip()
    {
        CreateFile("a.txt", 10);
        CreateFile(Path.Combine("sub", "b.txt"), 20);
        CreateFile(Path.Combine("sub", "deep", "c.txt"), 30);

        var (metadata, scanned) = await _scanner.ScanDirectoryAsync(_testRoot);
        var scannedRecords = scanned.ToList();

        await _database.InsertItemsAsync(scannedRecords);
        var loaded = await _database.FindItemsAsync<FileRecord>(r => r.RootFolderId == metadata.TreeId);

        var results = await _comparer.CompareAsync(metadata, scannedRecords, metadata, loaded);

        Assert.Equal(scannedRecords.Count, loaded.Count);
        Assert.Empty(results);
    }
}
