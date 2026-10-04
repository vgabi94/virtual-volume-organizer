using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class VolumeCommandTests : IAsyncLifetime
{
    private TempCatalogue _catalogue = null!;

    public async Task InitializeAsync() => _catalogue = await TempCatalogue.CreateAsync();

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private Task<CliResult> RunAsync(params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path]);

    private Task<CliResult> RunAsync(ScriptedTerminal terminal, params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path], terminal: terminal);

    private async Task<VirtualVolumeRecord> StoredAsync(Guid id) =>
        (await _catalogue.Volumes.GetVirtualVolumesAsync()).Single(volume => volume.Id == id);

    #region create

    [Fact]
    public async Task CreateWithANameAloneTakesTheDefaultIcon()
    {
        var result = await RunAsync("volume", "create", "Backups");

        Assert.Equal(0, result.ExitCode);
        var created = result.Json;
        Assert.Equal("Backups", created.GetProperty("name").GetString());
        Assert.Equal("HardDrive", created.GetProperty("icon").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("color").ValueKind);
        Assert.Equal(0, created.GetProperty("folderCount").GetInt32());

        var stored = await StoredAsync(created.GetProperty("id").GetGuid());
        Assert.Equal("Backups", stored.Name);
    }

    [Fact]
    public async Task CreateTakesAnIconAndAColour()
    {
        var result = await RunAsync("volume", "create", "Discs", "--icon", "compactdisc", "--color", "#FF8800");

        var stored = await StoredAsync(result.Json.GetProperty("id").GetGuid());
        Assert.Equal("CompactDisc", stored.Icon);
        Assert.Equal("#FF8800", stored.Color);
    }

    [Fact]
    public async Task CreateTrimsTheNameAsTheGuiDoes()
    {
        var result = await RunAsync("volume", "create", "  Backups  ");

        Assert.Equal("Backups", result.Json.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ACreatedVolumeIsListed()
    {
        var id = (await RunAsync("volume", "create", "Backups")).Json.GetProperty("id").GetGuid();

        var listed = (await RunAsync("volume", "list")).Json;

        Assert.Equal(id, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData("Backups", "Rocket", null)]
    [InlineData("Backups", "Folder", null)]
    [InlineData("Backups", null, "orange")]
    public async Task CreateWithAnythingInvalidIsAUsageErrorAndCreatesNothing(string name, string? icon, string? color)
    {
        string[] args = ["volume", "create", name];
        if (icon != null) args = [.. args, "--icon", icon];
        if (color != null) args = [.. args, "--color", color];

        var result = await RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
        Assert.Empty(await _catalogue.Volumes.GetVirtualVolumesAsync());
    }

    #endregion

    #region update

    [Fact]
    public async Task UpdatingTheNameLeavesTheRestAlone()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups", "CompactDisc", "#112233");

        var result = await RunAsync("volume", "update", volume.Id.ToString(), "--name", "Discs");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(volume with { Name = "Discs" }, await StoredAsync(volume.Id));
    }

    [Fact]
    public async Task UpdatingTheIconLeavesTheRestAlone()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups", "CompactDisc", "#112233");

        await RunAsync("volume", "update", volume.Id.ToString(), "--icon", "FloppyDisk");

        Assert.Equal(volume with { Icon = "FloppyDisk" }, await StoredAsync(volume.Id));
    }

    [Fact]
    public async Task UpdatingTheColourLeavesTheRestAlone()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups", "CompactDisc", "#112233");

        await RunAsync("volume", "update", volume.Id.ToString(), "--color", "#445566");

        Assert.Equal(volume with { Color = "#445566" }, await StoredAsync(volume.Id));
    }

    [Fact]
    public async Task ColourNoneClearsIt()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups", "CompactDisc", "#112233");

        var result = await RunAsync("volume", "update", volume.Id.ToString(), "--color", "none");

        Assert.Null((await StoredAsync(volume.Id)).Color);
        Assert.Equal(JsonValueKind.Null, result.Json.GetProperty("color").ValueKind);
    }

    [Fact]
    public async Task UpdateReportsTheFoldersTheVolumeHolds()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups");
        await _catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").Build());

        var result = await RunAsync("volume", "update", volume.Id.ToString(), "--name", "Old");

        Assert.Equal(1, result.Json.GetProperty("folderCount").GetInt32());
    }

    [Fact]
    public async Task UpdatingNothingIsAUsageError()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups");

        var result = await RunAsync("volume", "update", volume.Id.ToString());

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Theory]
    [InlineData("--name", "  ")]
    [InlineData("--icon", "Rocket")]
    [InlineData("--color", "#12")]
    public async Task UpdatingWithAnythingInvalidChangesNothing(string option, string value)
    {
        var volume = await _catalogue.AddVolumeAsync("Backups", "CompactDisc", "#112233");

        var result = await RunAsync("volume", "update", volume.Id.ToString(), option, value);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(volume, await StoredAsync(volume.Id));
    }

    [Fact]
    public async Task UpdatingAnUnknownVolumeIsNotFound()
    {
        var result = await RunAsync("volume", "update", Guid.NewGuid().ToString(), "--name", "x");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    #endregion

    #region delete

    [Fact]
    public async Task ConfirmedDeleteTakesTheFoldersAndTheTreesOnlyTheyUsed()
    {
        var backups = await _catalogue.AddVolumeAsync("Backups");
        var archive = await _catalogue.AddVolumeAsync("Archive");
        await _catalogue.AddFolderAsync(backups.Id, TestTree.Root("music").File("a.mp3", 1).Build());
        var shared = await _catalogue.AddFolderAsync(backups.Id, TestTree.Root("photos").File("b.jpg", 1).Build());
        await _catalogue.Volumes.CopyFolderAsync(shared.Id, archive.Id);

        var result = await RunAsync(ScriptedTerminal.Typing("delete"), "volume", "delete", backups.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(backups.Id, result.Json.GetProperty("deleted").GetProperty("id").GetGuid());
        Assert.Equal([archive.Id], (await _catalogue.Volumes.GetVirtualVolumesAsync()).Select(volume => volume.Id));

        // The tree the copy in Archive still stands on is kept; the one only Backups used is gone
        var names = (await _catalogue.Database.ReadItemsAsync<FileRecord>()).Select(record => record.Name).ToList();
        Assert.Contains("b.jpg", names);
        Assert.DoesNotContain("a.mp3", names);
    }

    [Fact]
    public async Task ThePromptIsTheGuisVolumeWarning()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups");
        await _catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").Build());
        await _catalogue.AddFolderAsync(volume.Id, TestTree.Root("music").Build());

        var result = await RunAsync(ScriptedTerminal.Typing("delete"), "volume", "delete", volume.Id.ToString());

        Assert.Contains("Delete Virtual Volume", result.Stderr);
        Assert.Contains("The 2 folders it holds are deleted with it.", result.Stderr);
    }

    [Theory]
    [InlineData(true, 6, "cancelled")]
    [InlineData(false, 4, "confirmation_required")]
    public async Task WithoutTheUsersWordNothingIsDeleted(bool interactive, int exitCode, string code)
    {
        var volume = await _catalogue.AddVolumeAsync("Backups");
        await _catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").File("a.jpg", 1).Build());
        var before = await _catalogue.SnapshotAsync();

        var terminal = interactive ? ScriptedTerminal.Typing("Delete") : ScriptedTerminal.Redirected();
        var result = await RunAsync(terminal, "volume", "delete", volume.Id.ToString());

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task WithoutATerminalTheAgentIsToldWhatWouldGo()
    {
        var volume = await _catalogue.AddVolumeAsync("Backups");
        await _catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").Build());

        var error = (await RunAsync("volume", "delete", volume.Id.ToString())).Error;

        Assert.StartsWith(
            "Deleting this virtual volume cannot be undone. The 1 folder it holds is deleted with it.",
            error.GetProperty("message").GetString());
        Assert.Equal(
            $"vvo volume delete {volume.Id} --db {_catalogue.Path}",
            error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task DeletingAnUnknownVolumeIsNotFoundWithoutAsking()
    {
        var terminal = ScriptedTerminal.Typing("delete");

        var result = await RunAsync(terminal, "volume", "delete", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(0, terminal.Reads);
    }

    #endregion
}
