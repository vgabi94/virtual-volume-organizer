using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class BrowseCommandTests : IAsyncLifetime
{
    // photos (D:\photos), listed in 'Backups':
    //   b.jpg, A.jpg, 2024\ (c.jpg, Trip\ (d.jpg)), empty\
    private static readonly FolderTree Photos = TestTree.Root("photos", path: @"D:\photos")
        .File("b.jpg", 10)
        .File("A.jpg", 20)
        .Folder("2024", year => year
            .File("c.jpg", 30)
            .Folder("Trip", trip => trip.File("d.jpg", 40)))
        .Folder("empty")
        .Build();

    private TempCatalogue _catalogue = null!;
    private VirtualVolumeRecord _backups = null!;
    private RootFolderMetadata _entry = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        _backups = await _catalogue.AddVolumeAsync("Backups");
        _entry = await _catalogue.AddFolderAsync(_backups.Id, Photos);
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private Task<CliResult> RunAsync(params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path]);

    private static string Id(string name) => Photos.Record(name).Id.ToString();

    private static IEnumerable<string?> Names(JsonElement records) =>
        records.EnumerateArray().Select(record => record.GetProperty("name").GetString());

    private static JsonElement OnlyPlacement(JsonElement record) =>
        Assert.Single(record.GetProperty("entries").EnumerateArray());

    #region ls

    [Fact]
    public async Task AnEntryIdListsTheTopOfItsTree()
    {
        var result = await RunAsync("ls", _entry.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("photos", result.Json.GetProperty("folder").GetProperty("name").GetString());
        Assert.Equal(["2024", "empty", "A.jpg", "b.jpg"], Names(result.Json.GetProperty("children")));
    }

    [Fact]
    public async Task AFolderRecordIdListsThatFolder()
    {
        var result = await RunAsync("ls", Id("2024"));

        Assert.Equal(["Trip", "c.jpg"], Names(result.Json.GetProperty("children")));
    }

    [Fact]
    public async Task ChildrenCarryTheirPathsAndIds()
    {
        var children = (await RunAsync("ls", Id("2024"))).Json.GetProperty("children");
        var c = children.EnumerateArray().Single(child => child.GetProperty("name").GetString() == "c.jpg");

        Assert.Equal(Photos.Record("c.jpg").Id, c.GetProperty("id").GetGuid());
        Assert.Equal(Photos.Record("2024").Id, c.GetProperty("parentId").GetGuid());
        Assert.Equal(30, c.GetProperty("size").GetInt64());

        var placement = OnlyPlacement(c);
        Assert.Equal(_entry.Id, placement.GetProperty("entryId").GetGuid());
        Assert.Equal(@"Backups:\photos\2024\c.jpg", placement.GetProperty("cataloguePath").GetString());
        Assert.Equal(@"D:\photos\2024\c.jpg", placement.GetProperty("physicalPath").GetString());
    }

    [Fact]
    public async Task AnEmptyFolderHasNoChildren()
    {
        var result = await RunAsync("ls", Id("empty"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Json.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public async Task ARecordInASharedTreeIsPlacedByEveryEntry()
    {
        var archive = await _catalogue.AddVolumeAsync("Archive");
        var copy = await _catalogue.Volumes.CopyFolderAsync(_entry.Id, archive.Id, "Old photos");

        var c = (await RunAsync("ls", Id("2024"))).Json.GetProperty("children")
            .EnumerateArray().Single(child => child.GetProperty("name").GetString() == "c.jpg");

        var placements = c.GetProperty("entries").EnumerateArray()
            .ToDictionary(p => p.GetProperty("entryId").GetGuid(), p => p.GetProperty("cataloguePath").GetString());

        Assert.Equal(2, placements.Count);
        Assert.Equal(@"Backups:\photos\2024\c.jpg", placements[_entry.Id]);
        Assert.Equal(@"Archive:\Old photos\2024\c.jpg", placements[copy.Id]);
    }

    [Fact]
    public async Task AnEntryIdPlacesThroughThatEntryAlone()
    {
        var archive = await _catalogue.AddVolumeAsync("Archive");
        var copy = await _catalogue.Volumes.CopyFolderAsync(_entry.Id, archive.Id, "Old photos");

        var children = (await RunAsync("ls", copy.Id.ToString())).Json.GetProperty("children");

        Assert.All(children.EnumerateArray(), child =>
            Assert.Equal(copy.Id, OnlyPlacement(child).GetProperty("entryId").GetGuid()));
    }

    [Fact]
    public async Task ListingAFileIsAUsageError()
    {
        var result = await RunAsync("ls", Id("c.jpg"));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task AnUnknownIdIsNotFound()
    {
        var result = await RunAsync("ls", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public async Task AnIdThatIsNotAGuidIsAUsageError()
    {
        var result = await RunAsync("ls", "photos");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    #endregion

    #region tree

    private static List<(int Depth, string? Name)> Below(JsonElement tree) =>
        tree.GetProperty("below").EnumerateArray()
            .Select(entry => (entry.GetProperty("depth").GetInt32(), entry.GetProperty("record").GetProperty("name").GetString()))
            .ToList();

    [Fact]
    public async Task EverythingBelowIsListedInTreeOrderWithItsDepth()
    {
        var tree = (await RunAsync("tree", _entry.Id.ToString())).Json;

        Assert.Equal("photos", tree.GetProperty("folder").GetProperty("name").GetString());
        Assert.Equal(
            [(1, "2024"), (2, "Trip"), (3, "d.jpg"), (2, "c.jpg"), (1, "empty"), (1, "A.jpg"), (1, "b.jpg")],
            Below(tree));
    }

    [Fact]
    public async Task EachEntryCarriesItsParentAndPaths()
    {
        var tree = (await RunAsync("tree", _entry.Id.ToString())).Json;

        var d = tree.GetProperty("below").EnumerateArray()
            .Single(entry => entry.GetProperty("record").GetProperty("name").GetString() == "d.jpg")
            .GetProperty("record");

        Assert.Equal(Photos.Record("Trip").Id, d.GetProperty("parentId").GetGuid());
        Assert.Equal(@"Backups:\photos\2024\Trip\d.jpg", OnlyPlacement(d).GetProperty("cataloguePath").GetString());
    }

    [Fact]
    public async Task DepthOneIsWhatLsLists()
    {
        var tree = (await RunAsync("tree", _entry.Id.ToString(), "--depth", "1")).Json;
        var ls = (await RunAsync("ls", _entry.Id.ToString())).Json;

        Assert.Equal(Names(ls.GetProperty("children")), Below(tree).Select(entry => entry.Name));
        Assert.All(Below(tree), entry => Assert.Equal(1, entry.Depth));
    }

    [Fact]
    public async Task DepthTwoStopsBelowTheSecondLevel()
    {
        var tree = (await RunAsync("tree", _entry.Id.ToString(), "--depth", "2")).Json;

        Assert.DoesNotContain(Below(tree), entry => entry.Name == "d.jpg");
        Assert.Contains((2, "Trip"), Below(tree));
    }

    [Fact]
    public async Task AnEmptyFolderHasNothingBelow()
    {
        var tree = (await RunAsync("tree", Id("empty"))).Json;

        Assert.Empty(Below(tree));
    }

    [Fact]
    public async Task ATreeOfASubfolderStartsThere()
    {
        var tree = (await RunAsync("tree", Id("Trip"))).Json;

        Assert.Equal("Trip", tree.GetProperty("folder").GetProperty("name").GetString());
        Assert.Equal([(1, "d.jpg")], Below(tree));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task ADepthBelowOneIsAUsageError(string depth)
    {
        var result = await RunAsync("tree", _entry.Id.ToString(), "--depth", depth);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task ATreeOfAFileIsAUsageError()
    {
        var result = await RunAsync("tree", Id("c.jpg"));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    #endregion

    #region stat

    [Fact]
    public async Task StatOfAFileHasNoChildCount()
    {
        var result = await RunAsync("stat", Id("d.jpg"));

        Assert.Equal(0, result.ExitCode);
        var record = result.Json.GetProperty("record");
        Assert.Equal("d.jpg", record.GetProperty("name").GetString());
        Assert.False(record.GetProperty("isFolder").GetBoolean());
        Assert.Equal(@"D:\photos\2024\Trip\d.jpg", OnlyPlacement(record).GetProperty("physicalPath").GetString());
        Assert.Equal(JsonValueKind.Null, result.Json.GetProperty("childCount").ValueKind);
    }

    [Fact]
    public async Task StatOfAFolderCountsWhatItHoldsDirectly()
    {
        var result = await RunAsync("stat", Id("2024"));

        Assert.Equal(2, result.Json.GetProperty("childCount").GetInt32());
        Assert.Equal(70, result.Json.GetProperty("record").GetProperty("size").GetInt64());
    }

    [Fact]
    public async Task StatOfAnEntryIsTheTopOfItsTree()
    {
        var result = await RunAsync("stat", _entry.Id.ToString());

        var record = result.Json.GetProperty("record");
        Assert.Equal(_entry.TreeId, record.GetProperty("id").GetGuid());
        Assert.Equal(@"Backups:\photos", OnlyPlacement(record).GetProperty("cataloguePath").GetString());
        Assert.Equal(4, result.Json.GetProperty("childCount").GetInt32());
    }

    [Fact]
    public async Task StatOfAnUnknownIdIsNotFound()
    {
        var result = await RunAsync("stat", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    #endregion
}
