using System.Text.Json;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class CompareCommandTests : IAsyncLifetime
{
    private static readonly DateTime Later = TestTree.DefaultTime.AddDays(1);

    private TempCatalogue _catalogue = null!;
    private VirtualVolumeRecord _volume = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        _volume = await _catalogue.AddVolumeAsync("Backups");
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private static TreeBuilder Code() => TestTree.Root("Code")
        .File("readme.md", 10)
        .File("same.txt", 5);

    private Task<RootFolderMetadata> AddAsync(FolderTree tree) => _catalogue.AddFolderAsync(_volume.Id, tree);

    private Task<CliResult> CompareAsync(params string[] args) =>
        CliRunner.RunAsync(["compare", .. args, "--db", _catalogue.Path]);

    private static List<JsonElement> Rows(CliResult result) =>
        result.Json.GetProperty("rows").EnumerateArray().ToList();

    private static JsonElement Row(CliResult result, string relativePath) =>
        Rows(result).Single(row => row.GetProperty("relativePath").GetString() == relativePath);

    // Scanned the way the GUI and the CLI scan, so a comparison with disk starts out even
    private async Task<RootFolderMetadata> CatalogueAsync(DiskTree disk, bool hidden = false)
    {
        var scan = await new FileScannerService().ScanDirectoryAsync(disk.Root, includeHiddenAndSystem: hidden);
        return await _catalogue.Volumes.AddFolderAsync(_volume.Id, scan.Metadata, scan.Records.ToList());
    }

    #region Two catalogued folders

    [Fact]
    public async Task IdenticalTreesHaveNoDifferences()
    {
        var left = await AddAsync(Code().Build());
        var right = await AddAsync(Code().Build());

        var result = await CompareAsync(left.Id.ToString(), right.Id.ToString(), "--exit-code");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(Rows(result));
        Assert.Equal(0, result.Json.GetProperty("counts").GetProperty("changed").GetInt32());
    }

    [Fact]
    public async Task EachKindOfDifferenceIsReported()
    {
        var left = await AddAsync(Code()
            .File("gone.txt", 1)
            .File("grown.txt", 1)
            .Build());

        var right = await AddAsync(Code()
            .File("grown.txt", 2, modified: Later)
            .File("new.txt", 3)
            .Folder("lib", lib => lib.File("a.cs", 1).File("b.cs", 1))
            .Build());

        var result = await CompareAsync(left.Id.ToString(), right.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("removed", Row(result, "gone.txt").GetProperty("status").GetString());
        Assert.Equal("added", Row(result, "new.txt").GetProperty("status").GetString());

        var grown = Row(result, "grown.txt");
        Assert.Equal("changed", grown.GetProperty("status").GetString());
        Assert.Equal(["size", "modified"], grown.GetProperty("changes").EnumerateArray().Select(change => change.GetString()));
        Assert.Equal(1, grown.GetProperty("left").GetProperty("size").GetInt64());
        Assert.Equal(2, grown.GetProperty("right").GetProperty("size").GetInt64());

        // A wholly new folder is one row standing for everything under it
        var lib = Row(result, "lib");
        Assert.Equal("added", lib.GetProperty("status").GetString());
        Assert.Equal(2, lib.GetProperty("descendantCount").GetInt32());

        var counts = result.Json.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("added").GetInt32());
        Assert.Equal(1, counts.GetProperty("removed").GetInt32());
    }

    [Fact]
    public async Task BothSidesAreDescribed()
    {
        var left = await AddAsync(Code().Build());
        var right = await AddAsync(TestTree.Root("Other", path: @"E:\Other").Build());

        var json = (await CompareAsync(left.Id.ToString(), right.Id.ToString())).Json;

        Assert.Equal(left.Id, json.GetProperty("left").GetProperty("entryId").GetGuid());
        Assert.Equal("Code", json.GetProperty("left").GetProperty("title").GetString());
        Assert.Equal(right.Id, json.GetProperty("right").GetProperty("entryId").GetGuid());
        Assert.Equal(@"E:\Other", json.GetProperty("right").GetProperty("path").GetString());
        Assert.Equal(0, json.GetProperty("skippedFolders").GetInt32());
    }

    [Fact]
    public async Task ExitCodeIsOneWhenThereAreDifferencesAndTheRowsStillCome()
    {
        var left = await AddAsync(Code().Build());
        var right = await AddAsync(Code().File("new.txt", 1).Build());

        var result = await CompareAsync(left.Id.ToString(), right.Id.ToString(), "--exit-code");

        Assert.Equal(1, result.ExitCode);
        Assert.Null(result.ErrorCode);
        Assert.Equal("added", Row(result, "new.txt").GetProperty("status").GetString());

        // The root is listed as changed for holding the new file, but is not counted as a change
        Assert.Equal("changed", Row(result, "").GetProperty("status").GetString());
        Assert.Equal(0, result.Json.GetProperty("counts").GetProperty("changed").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("counts").GetProperty("added").GetInt32());
    }

    [Fact]
    public async Task WithoutExitCodeDifferencesStillSucceed()
    {
        var left = await AddAsync(Code().Build());
        var right = await AddAsync(Code().File("new.txt", 1).Build());

        Assert.Equal(0, (await CompareAsync(left.Id.ToString(), right.Id.ToString())).ExitCode);
    }

    [Fact]
    public async Task IncludeUnchangedListsWhatIsTheSame()
    {
        var left = await AddAsync(Code().Build());
        var right = await AddAsync(Code().File("new.txt", 1).Build());

        var result = await CompareAsync(left.Id.ToString(), right.Id.ToString(), "--include-unchanged");

        Assert.Equal("unchanged", Row(result, "same.txt").GetProperty("status").GetString());
        Assert.Equal("added", Row(result, "new.txt").GetProperty("status").GetString());
        Assert.True(result.Json.GetProperty("counts").GetProperty("unchanged").GetInt32() > 0);
    }

    // The counts describe the comparison, not the rows chosen for listing
    [Fact]
    public async Task UnchangedIsCountedWhenNotListed()
    {
        var left = await AddAsync(Code().Build());
        var right = await AddAsync(Code().File("new.txt", 1).Build());

        var listed = await CompareAsync(left.Id.ToString(), right.Id.ToString(), "--include-unchanged");
        var unlisted = await CompareAsync(left.Id.ToString(), right.Id.ToString());

        Assert.DoesNotContain(unlisted.Json.GetProperty("rows").EnumerateArray(),
            row => row.GetProperty("status").GetString() == "unchanged");
        Assert.Equal(listed.Json.GetProperty("counts").ToString(), unlisted.Json.GetProperty("counts").ToString());
    }

    #endregion

    #region A catalogued folder and the disk

    [Fact]
    public async Task ChangesOnDiskSinceTheScanAreReported()
    {
        using var disk = new DiskTree()
            .File("keep.txt", 5)
            .File("delete-me.txt", 5)
            .File("grow.txt", 5);
        var entry = await CatalogueAsync(disk);

        File.Delete(disk.PathOf("delete-me.txt"));
        disk.File("grow.txt", 50).File(@"new\added.txt", 1);

        var result = await CompareAsync(entry.Id.ToString(), "--disk", disk.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("removed", Row(result, "delete-me.txt").GetProperty("status").GetString());
        Assert.Equal("changed", Row(result, "grow.txt").GetProperty("status").GetString());
        Assert.Equal("added", Row(result, "new").GetProperty("status").GetString());
        Assert.DoesNotContain(Rows(result), row => row.GetProperty("relativePath").GetString() == "keep.txt");
    }

    [Fact]
    public async Task TheDiskSideHasNoIdsAndNoEntry()
    {
        using var disk = new DiskTree().File("a.txt", 1);
        var entry = await CatalogueAsync(disk);
        disk.File("b.txt", 1);

        var result = await CompareAsync(entry.Id.ToString(), "--disk", disk.Root);

        Assert.Equal(JsonValueKind.Null, result.Json.GetProperty("right").GetProperty("entryId").ValueKind);
        Assert.Equal(disk.Root, result.Json.GetProperty("right").GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, Row(result, "b.txt").GetProperty("right").GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task AnUnchangedFolderOnDiskHasNoDifferences()
    {
        using var disk = new DiskTree().File("a.txt", 1).File(@"sub\b.txt", 2);
        var entry = await CatalogueAsync(disk);

        var result = await CompareAsync(entry.Id.ToString(), "--disk", disk.Root, "--exit-code");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(Rows(result));
    }

    [Fact]
    public async Task HiddenFilesCountOnlyWhenAskedFor()
    {
        using var disk = new DiskTree().File("a.txt", 1);
        var entry = await CatalogueAsync(disk);
        disk.File("secret.txt", 1).Hidden("secret.txt");

        var without = await CompareAsync(entry.Id.ToString(), "--disk", disk.Root);
        var with = await CompareAsync(entry.Id.ToString(), "--disk", disk.Root, "--hidden");

        Assert.Empty(Rows(without));
        Assert.Equal("added", Row(with, "secret.txt").GetProperty("status").GetString());
    }

    [Fact]
    public async Task ComparingWithDiskWritesNothing()
    {
        using var disk = new DiskTree().File("a.txt", 1);
        var entry = await CatalogueAsync(disk);
        disk.File("b.txt", 1);
        var before = await _catalogue.SnapshotAsync();

        await CompareAsync(entry.Id.ToString(), "--disk", disk.Root);

        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task CancellingTheScanReportsCancelledAndWritesNothing()
    {
        using var disk = new DiskTree().File("a.txt", 1);
        var entry = await CatalogueAsync(disk);
        var before = await _catalogue.SnapshotAsync();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await CliRunner.RunAsync(
            ["compare", entry.Id.ToString(), "--disk", disk.Root, "--db", _catalogue.Path],
            cancellationToken: cancelled.Token);

        Assert.Equal(6, result.ExitCode);
        Assert.Equal("cancelled", result.ErrorCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    #endregion

    #region Mistakes

    [Fact]
    public async Task AnUnknownEntryIsNotFound()
    {
        var left = await AddAsync(Code().Build());

        var result = await CompareAsync(left.Id.ToString(), Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public async Task ARecordIdIsNotAnEntry()
    {
        var tree = Code().Build();
        var left = await AddAsync(tree);

        var result = await CompareAsync(left.Id.ToString(), tree.Record("readme.md").Id.ToString());

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task BothAnotherEntryAndDiskIsAUsageError()
    {
        var left = await AddAsync(Code().Build());
        var right = await AddAsync(Code().Build());

        var result = await CompareAsync(left.Id.ToString(), right.Id.ToString(), "--disk", @"C:\");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task NeitherAnotherEntryNorDiskIsAUsageError()
    {
        var left = await AddAsync(Code().Build());

        var result = await CompareAsync(left.Id.ToString());

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task AMissingDiskFolderIsNotFound()
    {
        var left = await AddAsync(Code().Build());

        var result = await CompareAsync(left.Id.ToString(), "--disk", Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}"));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public async Task ADiskPathThatIsAFileIsAUsageError()
    {
        using var disk = new DiskTree().File("a.txt", 1);
        var left = await AddAsync(Code().Build());

        var result = await CompareAsync(left.Id.ToString(), "--disk", disk.PathOf("a.txt"));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    #endregion
}
