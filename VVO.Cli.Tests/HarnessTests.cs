using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class HarnessTests
{
    [Fact]
    public async Task HelpPrintsUsageAndSucceeds()
    {
        var result = await CliRunner.RunAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Stdout);
        Assert.Contains("vvo", result.Stdout);
    }

    [Fact]
    public async Task TempCatalogueIsRemovedOnDispose()
    {
        string path;
        using (var catalogue = await TempCatalogue.CreateAsync())
        {
            path = catalogue.Path;
            Assert.True(File.Exists(path));
        }

        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void DiskTreeIsRemovedOnDispose()
    {
        string root;
        using (var tree = new DiskTree().File(@"a\b.txt", 10))
        {
            root = tree.Root;
            Assert.True(File.Exists(tree.PathOf(@"a\b.txt")));
        }

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task SnapshotNoticesASingleChangedRecord()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var tree = TestTree.Root("photos").File("a.jpg", 10).File("b.jpg", 20).Build();
        await catalogue.AddFolderAsync(volume.Id, tree);

        var before = await catalogue.SnapshotAsync();
        Assert.Equal(before, await catalogue.SnapshotAsync());

        var record = tree.Record("a.jpg") with { Size = 11 };
        await catalogue.Database.UpdateItemsAsync<FileRecord>([record]);

        Assert.NotEqual(before, await catalogue.SnapshotAsync());
    }
}
