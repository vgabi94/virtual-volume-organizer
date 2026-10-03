using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class FolderCommandTests : IAsyncLifetime
{
    private static readonly DateTime Written = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private TempCatalogue _catalogue = null!;
    private VirtualVolumeRecord _backups = null!;
    private VirtualVolumeRecord _archive = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        _backups = await _catalogue.AddVolumeAsync("Backups");
        _archive = await _catalogue.AddVolumeAsync("Archive");
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

    private async Task<RootFolderMetadata> StoredAsync(Guid id) =>
        (await _catalogue.Database.FindItemsAsync<RootFolderMetadata>(entry => entry.Id == id)).Single();

    private async Task<IReadOnlyCollection<RootFolderMetadata>> EntriesAsync() =>
        await _catalogue.Database.ReadItemsAsync<RootFolderMetadata>();

    private Task<RootFolderMetadata> AddPhotosAsync() =>
        _catalogue.AddFolderAsync(_backups.Id, TestTree.Root("photos").File("a.jpg", 1).Build());

    #region scan

    [Fact]
    public async Task ScanCataloguesAFolderFromDisk()
    {
        using var disk = new DiskTree()
            .File("a.txt", 10, Written)
            .File(@"sub\b.txt", 20, Written)
            .Folder("empty");

        var result = await RunAsync("folder", "scan", disk.Root, "--volume", _backups.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        var folder = result.Json.GetProperty("folder");
        Assert.Equal(_backups.Id, folder.GetProperty("volumeId").GetGuid());
        Assert.Equal(Path.GetFileName(disk.Root), folder.GetProperty("title").GetString());
        Assert.Equal(disk.Root, folder.GetProperty("path").GetString());
        Assert.Equal(30, folder.GetProperty("size").GetInt64());
        Assert.Equal(4, folder.GetProperty("recordCount").GetInt32());
        Assert.Equal(0, result.Json.GetProperty("skippedFolders").GetInt32());

        var b = (await _catalogue.Database.ReadItemsAsync<FileRecord>()).Single(record => record.Name == "b.txt");
        Assert.Equal(20, b.Size);
        Assert.Equal(Written, b.Modified);
    }

    [Fact]
    public async Task ScanStoresHowTheFolderIsListed()
    {
        using var disk = new DiskTree().File("a.txt", 1);

        var result = await RunAsync(
            "folder", "scan", disk.Root, "--volume", _backups.Id.ToString(),
            "--label", "Holidays", "--description", "Summer", "--icon", "folderimage", "--color", "#3366CC");

        var stored = await StoredAsync(result.Json.GetProperty("folder").GetProperty("id").GetGuid());
        Assert.Equal("Holidays", stored.Label);
        Assert.Equal("Summer", stored.Description);
        Assert.Equal("FolderImage", stored.Icon);
        Assert.Equal("#3366CC", stored.Color);
    }

    [Fact]
    public async Task HiddenEntriesAreScannedOnlyWhenAskedFor()
    {
        using var disk = new DiskTree().File("a.txt", 1).File("secret.txt", 1).Hidden("secret.txt");

        var without = await RunAsync("folder", "scan", disk.Root, "--volume", _backups.Id.ToString());
        var with = await RunAsync("folder", "scan", disk.Root, "--volume", _backups.Id.ToString(), "--hidden");

        Assert.Equal(1, without.Json.GetProperty("folder").GetProperty("recordCount").GetInt32());
        Assert.Equal(2, with.Json.GetProperty("folder").GetProperty("recordCount").GetInt32());
    }

    [Fact]
    public async Task ScanningAMissingFolderStoresNothing()
    {
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync(
            "folder", "scan", Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}"), "--volume", _backups.Id.ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task ScanningIntoAnUnknownVolumeStoresNothing()
    {
        using var disk = new DiskTree().File("a.txt", 1);
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync("folder", "scan", disk.Root, "--volume", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task ScanningAFileIsAUsageError()
    {
        using var disk = new DiskTree().File("a.txt", 1);

        var result = await RunAsync("folder", "scan", disk.PathOf("a.txt"), "--volume", _backups.Id.ToString());

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(await EntriesAsync());
    }

    [Fact]
    public async Task ScanningWithAnInvalidIconStoresNothing()
    {
        using var disk = new DiskTree().File("a.txt", 1);

        var result = await RunAsync("folder", "scan", disk.Root, "--volume", _backups.Id.ToString(), "--icon", "HardDrive");

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(await EntriesAsync());
    }

    [Fact]
    public async Task ACancelledScanStoresNothing()
    {
        using var disk = new DiskTree().File("a.txt", 1);
        var before = await _catalogue.SnapshotAsync();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await CliRunner.RunAsync(
            ["folder", "scan", disk.Root, "--volume", _backups.Id.ToString(), "--db", _catalogue.Path],
            cancellationToken: cancelled.Token);

        Assert.Equal(6, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    #endregion

    #region update

    [Theory]
    [InlineData("--label", "Holidays")]
    [InlineData("--description", "Summer")]
    [InlineData("--icon", "FolderStar")]
    [InlineData("--color", "#112233")]
    public async Task UpdatingOneFieldLeavesTheOthersAlone(string option, string value)
    {
        var entry = await AddPhotosAsync();
        entry = await _catalogue.Volumes.UpdateFolderAsync(entry.Id, "Old", "Before", "FolderHeart", "#445566");

        var result = await RunAsync("folder", "update", entry.Id.ToString(), option, value);

        Assert.Equal(0, result.ExitCode);
        var expected = option switch
        {
            "--label" => entry with { Label = value },
            "--description" => entry with { Description = value },
            "--icon" => entry with { Icon = value },
            _ => entry with { Color = value }
        };
        Assert.Equal(expected, await StoredAsync(entry.Id));
    }

    [Fact]
    public async Task AnEmptyLabelClearsItAndTheScannedNameShows()
    {
        var entry = await AddPhotosAsync();
        await _catalogue.Volumes.UpdateFolderAsync(entry.Id, "Holidays", null, null, null);

        var result = await RunAsync("folder", "update", entry.Id.ToString(), "--label", "");

        Assert.Equal(0, result.ExitCode);
        Assert.Null((await StoredAsync(entry.Id)).Label);
        Assert.Equal("photos", result.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AnEmptyDescriptionClearsIt()
    {
        var entry = await AddPhotosAsync();
        await _catalogue.Volumes.UpdateFolderAsync(entry.Id, null, "Summer", null, null);

        await RunAsync("folder", "update", entry.Id.ToString(), "--description", "");

        Assert.Null((await StoredAsync(entry.Id)).Description);
    }

    [Fact]
    public async Task ColourNoneClearsIt()
    {
        var entry = await AddPhotosAsync();
        await _catalogue.Volumes.UpdateFolderAsync(entry.Id, null, null, null, "#112233");

        await RunAsync("folder", "update", entry.Id.ToString(), "--color", "none");

        Assert.Null((await StoredAsync(entry.Id)).Color);
    }

    [Fact]
    public async Task UpdatingNothingIsAUsageError()
    {
        var entry = await AddPhotosAsync();

        var result = await RunAsync("folder", "update", entry.Id.ToString());

        Assert.Equal(2, result.ExitCode);
    }

    [Theory]
    [InlineData("--icon", "Rocket")]
    [InlineData("--color", "blue")]
    public async Task UpdatingWithAnythingInvalidChangesNothing(string option, string value)
    {
        var entry = await AddPhotosAsync();

        var result = await RunAsync("folder", "update", entry.Id.ToString(), option, value);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(entry, await StoredAsync(entry.Id));
    }

    [Fact]
    public async Task UpdatingAnUnknownFolderIsNotFound()
    {
        var result = await RunAsync("folder", "update", Guid.NewGuid().ToString(), "--label", "x");

        Assert.Equal(3, result.ExitCode);
    }

    #endregion

    #region copy and move

    [Fact]
    public async Task ACopySharesTheTreeInAnotherVolume()
    {
        var entry = await AddPhotosAsync();

        var result = await RunAsync("folder", "copy", entry.Id.ToString(), "--to", _archive.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        var copy = result.Json;
        Assert.NotEqual(entry.Id, copy.GetProperty("id").GetGuid());
        Assert.Equal(_archive.Id, copy.GetProperty("volumeId").GetGuid());
        Assert.Equal(entry.TreeId, copy.GetProperty("treeId").GetGuid());
        Assert.Equal(2, (await EntriesAsync()).Count);
    }

    [Fact]
    public async Task CopyingIntoItsOwnVolumeDuplicatesItUnderANewLabel()
    {
        var entry = await AddPhotosAsync();

        var result = await RunAsync(
            "folder", "copy", entry.Id.ToString(), "--to", _backups.Id.ToString(), "--label", "Photos again");

        Assert.Equal(_backups.Id, result.Json.GetProperty("volumeId").GetGuid());
        Assert.Equal("Photos again", result.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AMovedFolderKeepsItsTree()
    {
        var entry = await AddPhotosAsync();

        var result = await RunAsync("folder", "move", entry.Id.ToString(), "--to", _archive.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(_archive.Id, result.Json.GetProperty("volumeId").GetGuid());
        Assert.Equal(entry with { VirtualVolumeId = _archive.Id }, await StoredAsync(entry.Id));
    }

    [Theory]
    [InlineData("copy", true)]
    [InlineData("copy", false)]
    [InlineData("move", true)]
    [InlineData("move", false)]
    public async Task AnUnknownFolderOrVolumeIsNotFoundAndChangesNothing(string verb, bool unknownFolder)
    {
        var entry = await AddPhotosAsync();
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync(
            "folder", verb,
            (unknownFolder ? Guid.NewGuid() : entry.Id).ToString(),
            "--to", (unknownFolder ? _archive.Id : Guid.NewGuid()).ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    #endregion

    #region delete

    [Fact]
    public async Task DeletingTheLastFolderOnATreeTakesTheTree()
    {
        var entry = await AddPhotosAsync();

        var result = await RunAsync(ScriptedTerminal.Typing("delete"), "folder", "delete", entry.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(entry.Id, result.Json.GetProperty("deleted").GetProperty("id").GetGuid());
        Assert.Empty(await EntriesAsync());
        Assert.Empty(await _catalogue.Database.ReadItemsAsync<FileRecord>());
    }

    [Fact]
    public async Task DeletingAFolderACopyStillUsesKeepsTheTree()
    {
        var entry = await AddPhotosAsync();
        var copy = await _catalogue.Volumes.CopyFolderAsync(entry.Id, _archive.Id);

        await RunAsync(ScriptedTerminal.Typing("delete"), "folder", "delete", entry.Id.ToString());

        Assert.Equal([copy.Id], (await EntriesAsync()).Select(item => item.Id));
        Assert.NotEmpty(await _catalogue.Database.ReadItemsAsync<FileRecord>());
    }

    [Fact]
    public async Task ThePromptNamesTheFolderAsItIsListed()
    {
        var entry = await AddPhotosAsync();
        await _catalogue.Volumes.UpdateFolderAsync(entry.Id, "Holidays", null, null, null);

        var result = await RunAsync(ScriptedTerminal.Typing("no"), "folder", "delete", entry.Id.ToString());

        Assert.Contains("Delete Folder", result.Stderr);
        Assert.Contains("Holidays", result.Stderr);
    }

    [Theory]
    [InlineData(true, 6)]
    [InlineData(false, 4)]
    public async Task WithoutTheUsersWordNothingIsDeleted(bool interactive, int exitCode)
    {
        await AddPhotosAsync();
        var entry = (await EntriesAsync()).Single();
        var before = await _catalogue.SnapshotAsync();

        var terminal = interactive ? ScriptedTerminal.Typing("") : ScriptedTerminal.Redirected();
        var result = await RunAsync(terminal, "folder", "delete", entry.Id.ToString());

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task DeletingAnUnknownFolderIsNotFoundWithoutAsking()
    {
        var terminal = ScriptedTerminal.Typing("delete");

        var result = await RunAsync(terminal, "folder", "delete", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(0, terminal.Reads);
    }

    #endregion
}
