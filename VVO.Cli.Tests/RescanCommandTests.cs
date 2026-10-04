using System.Security.AccessControl;
using System.Security.Principal;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Tests;

public class RescanCommandTests : IAsyncLifetime
{
    private TempCatalogue _catalogue = null!;
    private VirtualVolumeRecord _volume = null!;
    private DiskTree _disk = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        _volume = await _catalogue.AddVolumeAsync("Backups");
        _disk = new DiskTree()
            .File("keep.txt", 5)
            .File("delete-me.txt", 5)
            .File("grow.txt", 5);
    }

    public Task DisposeAsync()
    {
        _disk.Dispose();
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private async Task<RootFolderMetadata> CatalogueAsync(DiskTree disk)
    {
        var scan = await new FileScannerService().ScanDirectoryAsync(disk.Root);
        return await _catalogue.Volumes.AddFolderAsync(_volume.Id, scan.Metadata, scan.Records.ToList());
    }

    private void ChangeTheDisk()
    {
        File.Delete(_disk.PathOf("delete-me.txt"));
        _disk.File("grow.txt", 50).File("new.txt", 1);
    }

    private Task<CliResult> RescanAsync(ScriptedTerminal terminal, params string[] args) =>
        CliRunner.RunAsync(["folder", "rescan", .. args, "--db", _catalogue.Path], terminal: terminal);

    private async Task<Dictionary<string, long>> CataloguedAsync(Guid treeId) =>
        (await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.RootFolderId == treeId && record.ParentId != null))
            .ToDictionary(record => record.Name, record => record.Size);

    private async Task<RootFolderMetadata> StoredAsync(Guid id) =>
        (await _catalogue.Database.FindItemsAsync<RootFolderMetadata>(entry => entry.Id == id)).Single();

    [Fact]
    public async Task ConfirmedTheCatalogueTakesWhatIsOnDiskNow()
    {
        var entry = await CatalogueAsync(_disk);
        ChangeTheDisk();

        var result = await RescanAsync(ScriptedTerminal.Typing("update"), entry.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            new Dictionary<string, long> { ["keep.txt"] = 5, ["grow.txt"] = 50, ["new.txt"] = 1 },
            await CataloguedAsync(entry.TreeId));

        var counts = result.Json.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("added").GetInt32());
        Assert.Equal(1, counts.GetProperty("removed").GetInt32());
        Assert.Equal(1, counts.GetProperty("changed").GetInt32());
    }

    [Fact]
    public async Task TheTreeKeepsItsIdentityAndACopySeesTheNewContents()
    {
        var entry = await CatalogueAsync(_disk);
        var archive = await _catalogue.AddVolumeAsync("Archive");
        var copy = await _catalogue.Volumes.CopyFolderAsync(entry.Id, archive.Id);
        ChangeTheDisk();

        var result = await RescanAsync(ScriptedTerminal.Typing("update"), entry.Id.ToString());

        Assert.Equal(entry.TreeId, result.Json.GetProperty("folder").GetProperty("treeId").GetGuid());
        Assert.Equal(entry.TreeId, (await StoredAsync(copy.Id)).TreeId);
        Assert.Contains("new.txt", (await CataloguedAsync(entry.TreeId)).Keys);
    }

    [Fact]
    public async Task WithNothingChangedItStillAsksAndRecordsTheScan()
    {
        var entry = await CatalogueAsync(_disk);
        var before = await CataloguedAsync(entry.TreeId);
        var terminal = ScriptedTerminal.Typing("update");

        var result = await RescanAsync(terminal, entry.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, terminal.Reads);
        Assert.Contains("Nothing has changed since the last scan.", result.Stderr);
        Assert.Equal(before, await CataloguedAsync(entry.TreeId));
        Assert.True((await StoredAsync(entry.Id)).LastScanned > entry.LastScanned);
    }

    [Fact]
    public async Task AnotherPathIsReadAndRecorded()
    {
        var entry = await CatalogueAsync(_disk);
        using var moved = new DiskTree().File("keep.txt", 5).File("elsewhere.txt", 2);

        var result = await RescanAsync(ScriptedTerminal.Typing("update"), entry.Id.ToString(), moved.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(moved.Root, (await StoredAsync(entry.Id)).Path);
        Assert.Contains("elsewhere.txt", (await CataloguedAsync(entry.TreeId)).Keys);
    }

    [Fact]
    public async Task ThePromptShowsWhatWouldChange()
    {
        var entry = await CatalogueAsync(_disk);
        ChangeTheDisk();

        var result = await RescanAsync(ScriptedTerminal.Typing("no"), entry.Id.ToString());

        Assert.Contains("1 added, 1 removed, 1 changed:", result.Stderr);
        Assert.Contains("+ new.txt", result.Stderr);
        Assert.Contains("- delete-me.txt", result.Stderr);
        Assert.Contains("~ grow.txt (size", result.Stderr);
        Assert.Contains("Rescanning replaces what is catalogued for this folder", result.Stderr);
        Assert.Contains("Type update to confirm:", result.Stderr);
    }

    // A folder the scan is refused would be applied as deleted, which is why the warning comes first
    [Fact]
    public async Task AFolderTheScanCouldNotReadIsWarnedAboutBeforeAsking()
    {
        if (!OperatingSystem.IsWindows())
            return;

        _disk.File(@"locked\inside.txt", 1);
        var entry = await CatalogueAsync(_disk);

        var locked = new DirectoryInfo(_disk.PathOf("locked"));
        var denial = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);

        var security = locked.GetAccessControl();
        security.AddAccessRule(denial);
        locked.SetAccessControl(security);

        try
        {
            var interactive = await RescanAsync(ScriptedTerminal.Typing("no"), entry.Id.ToString());
            var redirected = await RescanAsync(ScriptedTerminal.Redirected(), entry.Id.ToString());

            var warning = interactive.Stderr.IndexOf("Some folders were not read", StringComparison.Ordinal);
            Assert.True(warning >= 0, interactive.Stderr);
            Assert.True(warning < interactive.Stderr.IndexOf("Type update to confirm:", StringComparison.Ordinal));
            Assert.Contains("1 folder under", interactive.Stderr);

            Assert.Equal(1, redirected.Error.GetProperty("skippedFolders").GetInt32());
        }
        finally
        {
            security.RemoveAccessRule(denial);
            locked.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task ALongListOfChangesIsCut()
    {
        var entry = await CatalogueAsync(_disk);
        for (var i = 0; i < 60; i++)
        {
            _disk.File($"new{i:00}.txt", 1);
        }

        var result = await RescanAsync(ScriptedTerminal.Typing("no"), entry.Id.ToString());

        Assert.Contains("60 added, 0 removed, 0 changed:", result.Stderr);
        Assert.Contains("and 10 more", result.Stderr);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("Update")]
    [InlineData("")]
    public async Task AnythingButUpdateChangesNothing(string typed)
    {
        var entry = await CatalogueAsync(_disk);
        ChangeTheDisk();
        var before = await _catalogue.SnapshotAsync();

        var result = await RescanAsync(ScriptedTerminal.Typing(typed), entry.Id.ToString());

        Assert.Equal(6, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task WithoutATerminalTheAgentIsToldWhatWouldChange()
    {
        var entry = await CatalogueAsync(_disk);
        ChangeTheDisk();
        var before = await _catalogue.SnapshotAsync();

        var result = await RescanAsync(ScriptedTerminal.Redirected(), entry.Id.ToString());

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());

        var error = result.Error;
        Assert.Equal(1, error.GetProperty("counts").GetProperty("added").GetInt32());
        Assert.Equal(1, error.GetProperty("counts").GetProperty("removed").GetInt32());
        Assert.Equal(0, error.GetProperty("skippedFolders").GetInt32());
        Assert.Equal(_disk.Root, error.GetProperty("path").GetString());
        Assert.Equal($"vvo folder rescan {entry.Id} --db {_catalogue.Path}", error.GetProperty("command").GetString());
        Assert.Equal(
            $"vvo compare {entry.Id} --disk {_disk.Root} --db {_catalogue.Path}",
            error.GetProperty("preview").GetString());
    }

    [Fact]
    public async Task AnUnknownFolderIsNotFoundBeforeAnythingIsRead()
    {
        var terminal = ScriptedTerminal.Typing("update");

        var result = await RescanAsync(terminal, Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(0, terminal.Reads);
    }

    [Fact]
    public async Task AFolderNoLongerOnDiskIsNotFound()
    {
        var entry = await CatalogueAsync(_disk);
        var before = await _catalogue.SnapshotAsync();

        var result = await RescanAsync(
            ScriptedTerminal.Typing("update"), entry.Id.ToString(), Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}"));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    [Fact]
    public async Task AFolderWithNoPathOnRecordNeedsOneGiven()
    {
        var scan = await new FileScannerService().ScanDirectoryAsync(_disk.Root);
        var entry = await _catalogue.Volumes.AddFolderAsync(
            _volume.Id, scan.Metadata with { Path = string.Empty }, scan.Records.ToList());

        var result = await RescanAsync(ScriptedTerminal.Typing("update"), entry.Id.ToString());

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task CancellingTheScanAsksNothingAndChangesNothing()
    {
        var entry = await CatalogueAsync(_disk);
        ChangeTheDisk();
        var before = await _catalogue.SnapshotAsync();
        var terminal = ScriptedTerminal.Typing("update");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await CliRunner.RunAsync(
            ["folder", "rescan", entry.Id.ToString(), "--db", _catalogue.Path], terminal: terminal,
            cancellationToken: cancelled.Token);

        Assert.Equal(6, result.ExitCode);
        Assert.Equal(0, terminal.Reads);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }
}
