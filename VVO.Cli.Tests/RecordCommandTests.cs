using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class RecordCommandTests : IAsyncLifetime
{
    // photos: a.jpg (10), 2024\ (b.jpg 20, trip\ (c.jpg 30)); music: song.mp3 (40)
    private static readonly FolderTree Photos = TestTree.Root("photos")
        .File("a.jpg", 10)
        .Folder("2024", year => year
            .File("b.jpg", 20)
            .Folder("trip", trip => trip.File("c.jpg", 30)))
        .Build();

    private static readonly FolderTree Music = TestTree.Root("music").File("song.mp3", 40).Build();

    private TempCatalogue _catalogue = null!;
    private RootFolderMetadata _photos = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        var volume = await _catalogue.AddVolumeAsync("Backups");
        _photos = await _catalogue.AddFolderAsync(volume.Id, Photos);
        await _catalogue.AddFolderAsync(volume.Id, Music);
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private Task<CliResult> RunAsync(params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path]);

    private Task<CliResult> RunAsync(ScriptedTerminal terminal, params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path], terminal: terminal);

    private static string Id(FolderTree tree, string name) => tree.Record(name).Id.ToString();

    private async Task<FileRecord?> RecordAsync(Guid id) =>
        (await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.Id == id)).SingleOrDefault();

    private async Task<long> SizeOfAsync(Guid id) => (await RecordAsync(id))!.Size;

    private async Task<List<string>> ChildrenOfAsync(Guid id) =>
        (await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.ParentId == id))
            .Select(record => record.Name).Order().ToList();

    #region add

    [Fact]
    public async Task AFileIsAddedUnderTheFolderAndItsAncestorsGrow()
    {
        using var disk = new DiskTree().File("new.jpg", 7);

        var result = await RunAsync("add", Id(Photos, "trip"), disk.PathOf("new.jpg"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["new.jpg"], result.Json.GetProperty("added").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal(["c.jpg", "new.jpg"], await ChildrenOfAsync(Photos.Record("trip").Id));

        Assert.Equal(37, await SizeOfAsync(Photos.Record("trip").Id));
        Assert.Equal(57, await SizeOfAsync(Photos.Record("2024").Id));
        Assert.Equal(67, await SizeOfAsync(_photos.TreeId));
        Assert.Equal(67, result.Json.GetProperty("tree").GetProperty("size").GetInt64());
    }

    [Fact]
    public async Task AFolderIsAddedWithEverythingUnderIt()
    {
        using var disk = new DiskTree().File(@"extra\x.jpg", 1).File(@"extra\deep\y.jpg", 2);

        var result = await RunAsync("add", Id(Photos, "2024"), disk.PathOf("extra"));

        Assert.Equal(0, result.ExitCode);
        var extra = (await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.Name == "extra")).Single();
        Assert.Equal(Photos.Record("2024").Id, extra.ParentId);
        Assert.Equal(_photos.TreeId, extra.RootFolderId);
        Assert.Equal(3, extra.Size);
        Assert.Equal(["deep", "x.jpg"], await ChildrenOfAsync(extra.Id));
        Assert.Equal(63, await SizeOfAsync(_photos.TreeId));
    }

    [Fact]
    public async Task FilesAndFoldersCanBeAddedTogether()
    {
        using var disk = new DiskTree().File("one.txt", 1).File(@"two\three.txt", 2);

        var result = await RunAsync("add", _photos.Id.ToString(), disk.PathOf("one.txt"), disk.PathOf("two"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("one.txt", await ChildrenOfAsync(_photos.TreeId));
        Assert.Contains("two", await ChildrenOfAsync(_photos.TreeId));
    }

    [Fact]
    public async Task ANameAlreadyThereIsSkippedWithWhatIsUnderIt()
    {
        using var disk = new DiskTree().File(@"2024\other.jpg", 5).File("fresh.jpg", 1);

        var result = await RunAsync("add", _photos.Id.ToString(), disk.PathOf("2024"), disk.PathOf("fresh.jpg"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["2024"], result.Json.GetProperty("skipped").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal(["fresh.jpg"], result.Json.GetProperty("added").EnumerateArray().Select(name => name.GetString()));
        Assert.Empty(await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.Name == "other.jpg"));
    }

    [Fact]
    public async Task AddingUnderAFileIsAUsageError()
    {
        using var disk = new DiskTree().File("new.jpg", 1);
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync("add", Id(Photos, "a.jpg"), disk.PathOf("new.jpg"));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task AddingUnderAnUnknownFolderIsNotFound()
    {
        using var disk = new DiskTree().File("new.jpg", 1);

        var result = await RunAsync("add", Guid.NewGuid().ToString(), disk.PathOf("new.jpg"));

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task AMissingPathAddsNothingAtAll()
    {
        using var disk = new DiskTree().File("new.jpg", 1);
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync("add", _photos.Id.ToString(), disk.PathOf("new.jpg"), disk.PathOf("missing.jpg"));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    #endregion

    #region rm

    [Fact]
    public async Task RemovingAFileShrinksItsAncestors()
    {
        var result = await RunAsync(ScriptedTerminal.Typing("delete"), "rm", Id(Photos, "c.jpg"));

        Assert.Equal(0, result.ExitCode);
        Assert.Null(await RecordAsync(Photos.Record("c.jpg").Id));
        Assert.Equal(0, await SizeOfAsync(Photos.Record("trip").Id));
        Assert.Equal(20, await SizeOfAsync(Photos.Record("2024").Id));
        Assert.Equal(30, await SizeOfAsync(_photos.TreeId));
    }

    [Fact]
    public async Task RemovingAFolderTakesEverythingUnderIt()
    {
        await RunAsync(ScriptedTerminal.Typing("delete"), "rm", Id(Photos, "2024"));

        foreach (var name in new[] { "2024", "b.jpg", "trip", "c.jpg" })
        {
            Assert.Null(await RecordAsync(Photos.Record(name).Id));
        }

        Assert.Equal(10, await SizeOfAsync(_photos.TreeId));
    }

    [Fact]
    public async Task AFolderAndAFileInsideItAreSubtractedOnce()
    {
        await RunAsync(ScriptedTerminal.Typing("delete"), "rm", Id(Photos, "trip"), Id(Photos, "c.jpg"));

        Assert.Equal(20, await SizeOfAsync(Photos.Record("2024").Id));
        Assert.Equal(30, await SizeOfAsync(_photos.TreeId));
    }

    [Fact]
    public async Task RecordsFromTwoTreesAreRemovedTogether()
    {
        var result = await RunAsync(ScriptedTerminal.Typing("delete"), "rm", Id(Photos, "a.jpg"), Id(Music, "song.mp3"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Json.GetProperty("trees").GetArrayLength());
        Assert.Equal(0, await SizeOfAsync(Music.Metadata.TreeId));
        Assert.Equal(50, await SizeOfAsync(_photos.TreeId));
    }

    [Theory]
    [InlineData(new[] { "a.jpg" }, "Delete File", "The file on disk is left alone.")]
    [InlineData(new[] { "trip" }, "Delete Folder", "every catalogued file and folder under it. The files on disk are left alone.")]
    [InlineData(new[] { "a.jpg", "b.jpg" }, "2 items", "The files on disk are left alone. This cannot be undone.")]
    [InlineData(new[] { "a.jpg", "trip" }, "2 items", "Folders take every catalogued file and folder under them.")]
    public async Task TheWarningFitsWhatIsBeingRemoved(string[] names, string heading, string message)
    {
        var result = await RunAsync(
            ScriptedTerminal.Typing("no"), ["rm", .. names.Select(name => Id(Photos, name))]);

        Assert.Contains(heading, result.Stderr);
        Assert.Contains(message, result.Stderr);
    }

    [Fact]
    public async Task TheTopOfATreeCannotBeRemovedThisWay()
    {
        var terminal = ScriptedTerminal.Typing("delete");
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync(terminal, "rm", Id(Photos, "a.jpg"), _photos.TreeId.ToString());

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
        Assert.Equal(0, terminal.Reads);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task AnUnknownIdRemovesNothingEvenAlongsideKnownOnes()
    {
        var terminal = ScriptedTerminal.Typing("delete");
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync(terminal, "rm", Id(Photos, "a.jpg"), Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(0, terminal.Reads);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Theory]
    [InlineData(true, 6)]
    [InlineData(false, 4)]
    public async Task WithoutTheUsersWordNothingIsRemoved(bool interactive, int exitCode)
    {
        var before = await _catalogue.SnapshotAsync();

        var terminal = interactive ? ScriptedTerminal.Typing("yes") : ScriptedTerminal.Redirected();
        var result = await RunAsync(terminal, "rm", Id(Photos, "2024"));

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    #endregion
}
