using System.Text.Json;
using VVO.Core.Models;

namespace VVO.Cli.Tests.Hardening;

// Catalogues are years old and files get cut short: reading one with damage in it should still work
public class DamagedCatalogueTests
{
    private static Task<CliResult> RunAsync(TempCatalogue catalogue, params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", catalogue.Path]);

    private static IEnumerable<Guid> FolderIds(CliResult list) =>
        list.Json.EnumerateArray().Select(folder => folder.GetProperty("id").GetGuid());

    private static IEnumerable<string?> HitNames(CliResult search) =>
        search.Json.GetProperty("hits").EnumerateArray().Select(hit => hit.GetProperty("name").GetString());

    public static IEnumerable<object[]> ReadingCommands => CommandMatrix.Cases
        .Where(item => !item.Writes && !item.Destructive && item.Path is not ("db new" or "db import"))
        .Select(item => new object[] { item });

    // No read is stopped by damage elsewhere in the catalogue
    [Theory]
    [MemberData(nameof(ReadingCommands))]
    public async Task EveryReadWorksBesideEveryKindOfDamage(MatrixCase command)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var catalogue = fixture.Catalogue;
        await Awkward.EntryWithoutTreeAsync(catalogue, fixture.Backups.Id);
        await Awkward.RecordWithoutParentAsync(catalogue, fixture.Backups.Id);
        await Awkward.EntryInMissingVolumeAsync(catalogue);
        await Awkward.OrphanTreeAsync(catalogue);
        await Awkward.ParentLoopAsync(catalogue, fixture.Backups.Id);

        var result = await CliRunner.RunAsync(command.Valid(fixture));

        Assert.True(result.ExitCode == 0, $"{command}: {result.Stdout}");
    }

    #region A folder entry whose tree is gone

    [Fact]
    public async Task AFolderWithoutItsTreeIsLeftOutAsTheSidebarLeavesItOut()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var lost = await Awkward.EntryWithoutTreeAsync(catalogue, volume.Id);

        var folders = await RunAsync(catalogue, "folder", "list");
        var volumes = await RunAsync(catalogue, "volume", "list");
        var ls = await RunAsync(catalogue, "ls", lost.Id.ToString());

        Assert.DoesNotContain(lost.Id, FolderIds(folders));
        Assert.Equal(0, volumes.Json[0].GetProperty("folderCount").GetInt32());
        Assert.Equal("not_found", ls.ErrorCode);
    }

    [Fact]
    public async Task AFolderWithoutItsTreeCanStillBeDeleted()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var lost = await Awkward.EntryWithoutTreeAsync(catalogue, volume.Id);

        var result = await CliRunner.RunAsync(
            ["folder", "delete", lost.Id.ToString(), "--db", catalogue.Path], terminal: ScriptedTerminal.Typing("delete"));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(await catalogue.Database.ReadItemsAsync<RootFolderMetadata>());
    }

    #endregion

    #region A record whose parent is gone

    [Fact]
    public async Task ARecordThatLostItsParentIsNamedWhenAskedFor()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var (_, orphan) = await Awkward.RecordWithoutParentAsync(catalogue, volume.Id);

        var stat = await RunAsync(catalogue, "stat", orphan.Id.ToString());

        Assert.Equal(1, stat.ExitCode);
        Assert.Equal("error", stat.ErrorCode);
        Assert.Contains(orphan.Id.ToString(), stat.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SearchLeavesOutWhatCannotBePlacedAndSaysHowMany()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var (entry, _) = await Awkward.RecordWithoutParentAsync(catalogue, volume.Id);
        await catalogue.AddFolderAsync(volume.Id, VVO.Tests.TestTree.Root("fine").File("orphan-not.txt").Build());

        var search = await RunAsync(catalogue, "search", "orphan");

        Assert.Equal(0, search.ExitCode);
        Assert.Equal(["orphan-not.txt"], HitNames(search));
        Assert.Equal(1, search.Json.GetProperty("unplaced").GetInt32());

        // What is left of the tree still lists
        Assert.Equal(0, (await RunAsync(catalogue, "ls", entry.Id.ToString())).ExitCode);
    }

    #endregion

    #region A folder entry whose volume is gone

    [Fact]
    public async Task AFolderInAVolumeThatIsGoneIsLeftOutAsTheSidebarLeavesItOut()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var stranded = await Awkward.EntryInMissingVolumeAsync(catalogue);

        var folders = await RunAsync(catalogue, "folder", "list");
        var search = await RunAsync(catalogue, "search", "s.txt");
        var ls = await RunAsync(catalogue, "ls", stranded.Id.ToString());

        Assert.DoesNotContain(stranded.Id, FolderIds(folders));
        Assert.Empty(HitNames(search));
        Assert.Equal("not_found", ls.ErrorCode);
    }

    [Fact]
    public async Task AFolderInAVolumeThatIsGoneCanStillBeDeleted()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var stranded = await Awkward.EntryInMissingVolumeAsync(catalogue);

        var result = await CliRunner.RunAsync(
            ["folder", "delete", stranded.Id.ToString(), "--db", catalogue.Path], terminal: ScriptedTerminal.Typing("delete"));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(await catalogue.Database.ReadItemsAsync<RootFolderMetadata>());
        Assert.Empty(await catalogue.Database.ReadItemsAsync<FileRecord>());
    }

    #endregion

    #region A tree no folder lists

    [Fact]
    public async Task ATreeNoFolderListsIsInNoListingButItsRecordsCanBeLookedAt()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var stray = await Awkward.OrphanTreeAsync(catalogue);

        var search = await RunAsync(catalogue, "search", "stray");
        var stat = await RunAsync(catalogue, "stat", stray.Record("stray.txt").Id.ToString());

        Assert.Empty(HitNames(search));
        Assert.Equal(0, stat.ExitCode);
        Assert.Equal(0, stat.Json.GetProperty("record").GetProperty("entries").GetArrayLength());
    }

    // With no place to head them, tables fall back on the folder's own name
    [Fact]
    public async Task ATreeNoFolderListsStillShowsAsATable()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var stray = await Awkward.OrphanTreeAsync(catalogue);
        var root = stray.Metadata.TreeId.ToString();

        var ls = await RunAsync(catalogue, "ls", root, "--format", "table");
        var tree = await RunAsync(catalogue, "tree", root, "--format", "table");

        Assert.Equal(0, ls.ExitCode);
        Assert.StartsWith("orphan", ls.Stdout);
        Assert.Contains("stray.txt", ls.Stdout);
        Assert.Equal(0, tree.ExitCode);
        Assert.Contains("stray.txt", tree.Stdout);
    }

    #endregion

    #region Folders that are each other's parent

    // A walk up from inside the loop never reaches a root, so it has to notice it has been round
    [Fact]
    public async Task ALoopOfParentsIsReportedRatherThanWalkedForever()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var (entry, tree) = await Awkward.ParentLoopAsync(catalogue, volume.Id);
        var inside = tree.Record("inside.txt").Id.ToString();
        var limit = TimeSpan.FromSeconds(30);

        var stat = await RunAsync(catalogue, "stat", inside).WaitAsync(limit);
        var ls = await RunAsync(catalogue, "ls", tree.Record("b").Id.ToString()).WaitAsync(limit);
        var search = await RunAsync(catalogue, "search", "inside").WaitAsync(limit);
        var listing = await RunAsync(catalogue, "tree", entry.Id.ToString()).WaitAsync(limit);

        Assert.Equal("error", stat.ErrorCode);
        Assert.Contains(inside, stat.Error.GetProperty("message").GetString());
        Assert.Equal("error", ls.ErrorCode);
        Assert.Empty(HitNames(search));
        Assert.Equal(1, search.Json.GetProperty("unplaced").GetInt32());
        Assert.Equal(0, listing.ExitCode);
    }

    #endregion

    // LiteDB writes changes through a log file beside the catalogue; one left by a crash, even an
    // empty one, must not stop the catalogue opening
    [Fact]
    public async Task ALeftoverLogFileDoesNotStopTheCatalogue()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        await catalogue.AddVolumeAsync("Backups");
        var log = Path.Combine(
            Path.GetDirectoryName(catalogue.Path)!,
            $"{Path.GetFileNameWithoutExtension(catalogue.Path)}-log{Path.GetExtension(catalogue.Path)}");
        File.WriteAllBytes(log, []);

        var result = await RunAsync(catalogue, "volume", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetArrayLength());
    }
}
