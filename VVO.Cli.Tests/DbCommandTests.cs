using System.Text.Json;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class DbCommandTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"VVO_DbCommands_{Guid.NewGuid()}");

    public DbCommandTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private string PathOf(string name) => Path.Combine(_directory, name);

    private static async Task<IReadOnlyCollection<DatabaseMetadata>> MetadataOf(string path)
    {
        var database = new DatabaseService();
        await database.EnsureDatabaseReadyAsync(path);
        return await database.ReadItemsAsync<DatabaseMetadata>();
    }

    #region db new

    [Fact]
    public async Task NewCreatesACatalogueNamedAfterItsFile()
    {
        var path = PathOf("Backups.vvo");

        var result = await CliRunner.RunAsync("db", "new", path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(path, result.Json.GetProperty("path").GetString());
        Assert.Equal("Backups", result.Json.GetProperty("name").GetString());

        var metadata = Assert.Single(await MetadataOf(path));
        Assert.Equal("Backups", metadata.Name);
        Assert.Equal(path, metadata.Path);
    }

    [Fact]
    public async Task NewTakesTheNameItIsGiven()
    {
        var path = PathOf("b.vvo");

        var result = await CliRunner.RunAsync("db", "new", path, "--name", "Home backups");

        Assert.Equal("Home backups", result.Json.GetProperty("name").GetString());
        Assert.Equal("Home backups", Assert.Single(await MetadataOf(path)).Name);
    }

    [Fact]
    public async Task NewOverAFileTheUserAgreesToReplaceLeavesAnEmptyCatalogue()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Old");
        await catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").File("a.jpg", 1).Build());

        var result = await CliRunner.RunAsync(
            ["db", "new", catalogue.Path], terminal: ScriptedTerminal.Typing("replace"));

        Assert.Equal(0, result.ExitCode);

        var info = (await CliRunner.RunAsync("db", "info", "--db", catalogue.Path)).Json;
        Assert.Equal(0, info.GetProperty("volumeCount").GetInt32());
        Assert.Equal(0, info.GetProperty("recordCount").GetInt32());
    }

    [Theory]
    [InlineData(true, 6, "cancelled")]
    [InlineData(false, 4, "confirmation_required")]
    public async Task NewLeavesAnExistingFileAloneWithoutTheUsersWord(bool interactive, int exitCode, string code)
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        await catalogue.AddVolumeAsync("Keep");
        var before = File.ReadAllBytes(catalogue.Path);

        var terminal = interactive ? ScriptedTerminal.Typing("yes") : ScriptedTerminal.Redirected();
        var result = await CliRunner.RunAsync(["db", "new", catalogue.Path], terminal: terminal);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(before, File.ReadAllBytes(catalogue.Path));
    }

    [Fact]
    public async Task NewHandsTheUserTheCommandWithTheFullPath()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, catalogue.Path);

        var result = await CliRunner.RunAsync("db", "new", relative);

        Assert.Equal($"vvo db new {catalogue.Path}", result.Error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task NewInAFolderThatDoesNotExistIsNotFound()
    {
        var path = PathOf(@"missing\b.vvo");

        var result = await CliRunner.RunAsync("db", "new", path);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task NewWithAnEmptyPathIsAUsageError()
    {
        var result = await CliRunner.RunAsync("db", "new", "");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task NewOntoAFolderIsAUsageError()
    {
        var result = await CliRunner.RunAsync("db", "new", _directory);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    #endregion

    #region db info

    [Fact]
    public async Task InfoOfANewCatalogueCountsNothing()
    {
        var path = PathOf("Fresh.vvo");
        await CliRunner.RunAsync("db", "new", path);

        var result = await CliRunner.RunAsync("db", "info", "--db", path);

        Assert.Equal(0, result.ExitCode);
        var info = result.Json;
        Assert.Equal(path, info.GetProperty("path").GetString());
        Assert.Equal("Fresh", info.GetProperty("name").GetString());
        Assert.Equal(new FileInfo(path).Length, info.GetProperty("fileSize").GetInt64());
        Assert.Equal(0, info.GetProperty("volumeCount").GetInt32());
        Assert.Equal(0, info.GetProperty("folderCount").GetInt32());
        Assert.Equal(0, info.GetProperty("treeCount").GetInt32());
        Assert.Equal(0, info.GetProperty("recordCount").GetInt32());
    }

    [Fact]
    public async Task InfoCountsWhatTheCatalogueHolds()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var backups = await catalogue.AddVolumeAsync("Backups");
        var archive = await catalogue.AddVolumeAsync("Archive");

        var photos = await catalogue.AddFolderAsync(backups.Id, TestTree.Root("photos")
            .File("a.jpg", 1)
            .Folder("2024", year => year.File("b.jpg", 2))
            .Build());
        await catalogue.AddFolderAsync(archive.Id, TestTree.Root("music").File("song.mp3", 3).Build());

        // A copy is a second entry on the same tree, not a second tree
        await catalogue.Volumes.CopyFolderAsync(photos.Id, archive.Id);

        var info = (await CliRunner.RunAsync("db", "info", "--db", catalogue.Path)).Json;

        Assert.Equal(2, info.GetProperty("volumeCount").GetInt32());
        Assert.Equal(3, info.GetProperty("folderCount").GetInt32());
        Assert.Equal(2, info.GetProperty("treeCount").GetInt32());
        Assert.Equal(4, info.GetProperty("recordCount").GetInt32());
    }

    [Fact]
    public async Task InfoOfACatalogueWithoutANameSaysSo()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        var info = (await CliRunner.RunAsync("db", "info", "--db", catalogue.Path)).Json;

        Assert.Equal(JsonValueKind.Null, info.GetProperty("name").ValueKind);
    }

    [Fact]
    public async Task InfoLastWriteIsUtc()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        var info = (await CliRunner.RunAsync("db", "info", "--db", catalogue.Path)).Json;

        Assert.EndsWith("Z", info.GetProperty("lastWrite").GetString());
    }

    [Fact]
    public async Task InfoOfAMissingCatalogueIsNotFound()
    {
        var result = await CliRunner.RunAsync("db", "info", "--db", PathOf("missing.vvo"));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    #endregion
}
