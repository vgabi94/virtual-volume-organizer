using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class SearchCommandTests : IAsyncLifetime
{
    private static readonly FolderTree Photos = TestTree.Root("photos", path: @"D:\photos")
        .File("beach.jpg", 10)
        .Folder("beach trip", trip => trip
            .Folder("day 1", day => day.File("Beach-sunset.jpg", 20)))
        .File("notes.txt", 5)
        .Build();

    private static readonly FolderTree Music = TestTree.Root("music", path: @"E:\music")
        .File("beach boys.mp3", 30)
        .Build();

    private TempCatalogue _catalogue = null!;
    private RootFolderMetadata _photos = null!;
    private RootFolderMetadata _music = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        var backups = await _catalogue.AddVolumeAsync("Backups");
        _photos = await _catalogue.AddFolderAsync(backups.Id, Photos);
        _music = await _catalogue.AddFolderAsync(backups.Id, Music);
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private Task<CliResult> SearchAsync(params string[] args) =>
        CliRunner.RunAsync(["search", .. args, "--db", _catalogue.Path]);

    private static List<JsonElement> Hits(CliResult result) =>
        result.Json.GetProperty("hits").EnumerateArray().ToList();

    private static IEnumerable<string?> Names(CliResult result) =>
        Hits(result).Select(hit => hit.GetProperty("name").GetString());

    [Fact]
    public async Task FilesAndFoldersAcrossEveryTreeAreFound()
    {
        var result = await SearchAsync("beach");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["beach trip", "beach boys.mp3", "Beach-sunset.jpg", "beach.jpg"], Names(result));
        Assert.Equal(4, result.Json.GetProperty("total").GetInt32());
        Assert.False(result.Json.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task AHitCarriesThePathsOfWhereItLives()
    {
        var sunset = Hits(await SearchAsync("sunset")).Single();

        var placement = Assert.Single(sunset.GetProperty("entries").EnumerateArray());
        Assert.Equal(_photos.Id, placement.GetProperty("entryId").GetGuid());
        Assert.Equal(@"Backups:\photos\beach trip\day 1\Beach-sunset.jpg", placement.GetProperty("cataloguePath").GetString());
        Assert.Equal(@"D:\photos\beach trip\day 1\Beach-sunset.jpg", placement.GetProperty("physicalPath").GetString());
    }

    // LiteDB compares names without regard to case, and the GUI runs the same query
    [Fact]
    public async Task CaseIsIgnoredAsTheGuiIgnoresIt()
    {
        Assert.Equal(Names(await SearchAsync("beach")), Names(await SearchAsync("BEACH")));
    }

    [Fact]
    public async Task FolderLimitsTheSearchToOneTree()
    {
        var result = await SearchAsync("beach", "--folder", _music.Id.ToString());

        Assert.Equal(["beach boys.mp3"], Names(result));
    }

    [Fact]
    public async Task ARecordInASharedTreeIsAHitOncePerEntry()
    {
        var archive = await _catalogue.AddVolumeAsync("Archive");
        var copy = await _catalogue.Volumes.CopyFolderAsync(_photos.Id, archive.Id, "Old photos");

        var hits = Hits(await SearchAsync("sunset"));

        Assert.Equal(2, hits.Count);
        var paths = hits.Select(hit => Assert.Single(hit.GetProperty("entries").EnumerateArray()))
            .ToDictionary(p => p.GetProperty("entryId").GetGuid(), p => p.GetProperty("cataloguePath").GetString());
        Assert.Equal(@"Backups:\photos\beach trip\day 1\Beach-sunset.jpg", paths[_photos.Id]);
        Assert.Equal(@"Archive:\Old photos\beach trip\day 1\Beach-sunset.jpg", paths[copy.Id]);
    }

    [Fact]
    public async Task FolderSearchPlacesHitsThroughThatEntryAlone()
    {
        var archive = await _catalogue.AddVolumeAsync("Archive");
        var copy = await _catalogue.Volumes.CopyFolderAsync(_photos.Id, archive.Id);

        var hit = Hits(await SearchAsync("sunset", "--folder", copy.Id.ToString())).Single();

        Assert.Equal(copy.Id, Assert.Single(hit.GetProperty("entries").EnumerateArray()).GetProperty("entryId").GetGuid());
    }

    [Fact]
    public async Task TheTopOfATreeIsNeverAHit()
    {
        Assert.Empty(Names(await SearchAsync("photos")));
        Assert.Empty(Names(await SearchAsync("photos", "--folder", _photos.Id.ToString())));
    }

    [Fact]
    public async Task ATreeNoFolderIsListedWithIsLeftOut()
    {
        var stray = TestTree.Root("stray").File("beach-stray.jpg").Build();
        await _catalogue.Database.InsertItemsAsync(stray.Records.ToList());

        Assert.DoesNotContain("beach-stray.jpg", Names(await SearchAsync("beach")));
    }

    [Fact]
    public async Task NothingFoundIsAnEmptyList()
    {
        var result = await SearchAsync("zebra");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(Hits(result));
        Assert.Equal(0, result.Json.GetProperty("total").GetInt32());
        Assert.False(result.Json.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task ALimitBelowTheHitsTruncates()
    {
        var result = await SearchAsync("beach", "--limit", "2");

        Assert.Equal(["beach trip", "beach boys.mp3"], Names(result));
        Assert.Equal(4, result.Json.GetProperty("total").GetInt32());
        Assert.True(result.Json.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task ALimitOfExactlyTheHitsDoesNotTruncate()
    {
        var result = await SearchAsync("beach", "--limit", "4");

        Assert.Equal(4, Hits(result).Count);
        Assert.False(result.Json.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyTermIsAUsageError(string term)
    {
        var result = await SearchAsync(term);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task ALimitBelowOneIsAUsageError(string limit)
    {
        var result = await SearchAsync("beach", "--limit", limit);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task AnUnknownFolderIsNotFound()
    {
        var result = await SearchAsync("beach", "--folder", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public async Task FolderTakesAnEntryIdNotARecordId()
    {
        var result = await SearchAsync("beach", "--folder", Photos.Record("beach trip").Id.ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }
}
