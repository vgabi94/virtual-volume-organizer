using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Tests;

// The search behind the GUI explorer and the CLI alike
public class FindByNameTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
    private readonly DatabaseService _database = new();
    private VirtualVolumeService _service = null!;
    private FolderTree _tree = null!;

    public async Task InitializeAsync()
    {
        await _database.EnsureDatabaseReadyAsync(_dbPath);
        _service = new VirtualVolumeService(_database);

        _tree = TestTree.Root("files")
            .File("a_b.txt")
            .File("axb.txt")
            .File("50%.txt")
            .File("500.txt")
            .File("[a].txt")
            .File("a.txt")
            .File("it's.txt")
            .File("Report.PDF")
            .Build();

        var volume = await _service.CreateVirtualVolumeAsync("Backups", "HardDrive");
        await _service.AddFolderAsync(volume.Id, _tree.Metadata, _tree.Records.ToList());
    }

    public Task DisposeAsync()
    {
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private async Task<IEnumerable<string>> NamesAsync(string term, Guid? treeId = null) =>
        (await _service.FindByNameAsync(term, treeId)).Select(record => record.Name).Order();

    [Fact]
    public async Task AnUnderscoreIsAnUnderscore()
    {
        Assert.Equal(["a_b.txt"], await NamesAsync("a_b"));
    }

    [Fact]
    public async Task APercentSignIsAPercentSign()
    {
        Assert.Equal(["50%.txt"], await NamesAsync("50%"));
        Assert.Empty(await NamesAsync("%%"));
    }

    [Fact]
    public async Task BracketsAndQuotesAreThemselves()
    {
        Assert.Equal(["[a].txt"], await NamesAsync("[a]"));
        Assert.Equal(["it's.txt"], await NamesAsync("it's"));
    }

    [Fact]
    public async Task CaseIsStillIgnored()
    {
        Assert.Equal(["Report.PDF"], await NamesAsync("report.pdf"));
        Assert.Equal(await NamesAsync("a_b"), await NamesAsync("A_B"));
    }

    [Fact]
    public async Task WithinOneTreeTheRootIsNeverAHit()
    {
        Assert.Empty(await NamesAsync("files", _tree.Metadata.TreeId));
        Assert.Equal(["a_b.txt"], await NamesAsync("a_b", _tree.Metadata.TreeId));
    }

    [Fact]
    public async Task EverywhereTheRootsAreNeverHits()
    {
        Assert.Empty(await NamesAsync("files"));
    }

    [Fact]
    public async Task WithinAnotherTreeNothingIsFound()
    {
        Assert.Empty(await NamesAsync("a_b", Guid.NewGuid()));
    }
}
