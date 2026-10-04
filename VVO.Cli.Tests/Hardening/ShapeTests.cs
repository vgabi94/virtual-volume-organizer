using System.Diagnostics;
using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

// Catalogues shaped the way real disks are, rather than the way tests usually are
public class ShapeTests : IAsyncLifetime
{
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

    private Task<CliResult> RunAsync(params string[] args) => CliRunner.RunAsync([.. args, "--db", _catalogue.Path]);

    private Task<CliResult> RunAsync(ScriptedTerminal terminal, params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path], terminal: terminal);

    private static List<string?> Names(JsonElement records) =>
        records.EnumerateArray().Select(record => record.GetProperty("name").GetString()).ToList();

    private static List<string?> Strings(JsonElement list) => list.EnumerateArray().Select(item => item.GetString()).ToList();

    #region Size

    [Fact]
    public async Task AFolderOfTwentyThousandFilesListsAndSearchesInGoodTime()
    {
        var entry = await _catalogue.AddFolderAsync(_volume.Id, Awkward.Wide(20_000));
        var watch = Stopwatch.StartNew();

        var ls = await RunAsync("ls", entry.Id.ToString());
        var search = await RunAsync("search", "file", "--limit", "50");

        Assert.Equal(20_000, ls.Json.GetProperty("children").GetArrayLength());
        Assert.Equal(50, search.Json.GetProperty("hits").GetArrayLength());
        Assert.Equal(20_000, search.Json.GetProperty("total").GetInt32());
        Assert.True(search.Json.GetProperty("truncated").GetBoolean());

        // The limit caps what is written, not only what is counted
        Assert.True(search.Stdout.Length < 100_000, $"{search.Stdout.Length} characters for 50 hits.");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Took {watch.Elapsed}.");
    }

    #endregion

    #region Names that differ only in case, as a case-sensitive disk allows

    [Fact]
    public async Task NamesThatDifferOnlyInCaseListInTheSameOrderEveryTime()
    {
        var entry = await _catalogue.AddFolderAsync(_volume.Id, TestTree.Root("mixed")
            .File("b.txt").File("a.txt").File("B.txt").File("A.txt").Build());

        var first = Names((await RunAsync("ls", entry.Id.ToString())).Json.GetProperty("children"));
        var second = Names((await RunAsync("ls", entry.Id.ToString())).Json.GetProperty("children"));

        Assert.Equal(["A.txt", "a.txt", "B.txt", "b.txt"], first);
        Assert.Equal(first, second);
    }

    // Core takes a name already there without regard to case, as Windows does
    [Fact]
    public async Task AddingANameThatDiffersOnlyInCaseIsSkipped()
    {
        var entry = await _catalogue.AddFolderAsync(_volume.Id, TestTree.Root("files").File("report.txt").Build());
        using var disk = new DiskTree().File("REPORT.txt", 1);

        var result = await RunAsync("add", entry.Id.ToString(), disk.PathOf("REPORT.txt"));

        Assert.Equal(["REPORT.txt"], Strings(result.Json.GetProperty("skipped")));
        Assert.Empty(Strings(result.Json.GetProperty("added")));
    }

    #endregion

    #region add given the same name twice

    [Fact]
    public async Task TheSameFolderGivenTwiceIsAddedOnceAndSkippedOnce()
    {
        var entry = await _catalogue.AddFolderAsync(_volume.Id, TestTree.Root("files").Build());
        using var disk = new DiskTree().File(@"photos\a.jpg", 1);

        var result = await RunAsync("add", entry.Id.ToString(), disk.PathOf("photos"), disk.PathOf("photos"));

        Assert.Equal(["photos"], Strings(result.Json.GetProperty("added")));
        Assert.Equal(["photos"], Strings(result.Json.GetProperty("skipped")));
        Assert.Single(await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.Name == "a.jpg"));
    }

    [Fact]
    public async Task TwoFoldersOfOneNameFromDifferentPlacesAreAddedOnceAndSkippedOnce()
    {
        var entry = await _catalogue.AddFolderAsync(_volume.Id, TestTree.Root("files").Build());
        using var disk = new DiskTree().File(@"one\photos\a.jpg", 1).File(@"two\photos\b.jpg", 1);

        var result = await RunAsync("add", entry.Id.ToString(), disk.PathOf(@"one\photos"), disk.PathOf(@"two\photos"));

        Assert.Equal(["photos"], Strings(result.Json.GetProperty("added")));
        Assert.Equal(["photos"], Strings(result.Json.GetProperty("skipped")));
        Assert.Single(await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.Name == "a.jpg"));
        Assert.Empty(await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.Name == "b.jpg"));
    }

    #endregion

    #region rm

    [Fact]
    public async Task AnIdGivenTwiceIsRemovedOnce()
    {
        var tree = TestTree.Root("files").File("a.txt", 5).File("b.txt", 7).Build();
        await _catalogue.AddFolderAsync(_volume.Id, tree);
        var id = tree.Record("a.txt").Id.ToString();

        var result = await RunAsync(ScriptedTerminal.Typing("delete"), "rm", id, id);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Json.GetProperty("removed").EnumerateArray());
        Assert.Contains("Delete File", result.Stderr);
        Assert.Equal(7, (await _catalogue.Database.FindItemsAsync<FileRecord>(record => record.Id == tree.Metadata.TreeId)).Single().Size);
    }

    [Fact]
    public async Task RemovingFromASharedScanSaysItGoesFromTheOtherFoldersToo()
    {
        var tree = TestTree.Root("files").File("a.txt").Build();
        var entry = await _catalogue.AddFolderAsync(_volume.Id, tree);
        var archive = await _catalogue.AddVolumeAsync("Archive");
        var copy = await _catalogue.Volumes.CopyFolderAsync(entry.Id, archive.Id);
        await _catalogue.Volumes.CopyFolderAsync(entry.Id, archive.Id);
        var id = tree.Record("a.txt").Id.ToString();

        var asked = await RunAsync("rm", id);
        Assert.Contains("It goes from the 2 other folders listing the same scan too.",
            asked.Error.GetProperty("message").GetString());

        var removed = await RunAsync(ScriptedTerminal.Typing("delete"), "rm", id);
        Assert.Contains("It goes from the 2 other folders listing the same scan too.", removed.Stderr);

        // And it does: the copy lists nothing now
        Assert.Equal(0, (await RunAsync("ls", copy.Id.ToString())).Json.GetProperty("children").GetArrayLength());
    }

    // A tree no folder lists must not cancel out the other folder of a shared one
    [Fact]
    public async Task RemovingFromASharedScanAndAnUnlistedOneStillCountsTheOtherFolder()
    {
        var tree = TestTree.Root("files").File("a.txt").Build();
        var entry = await _catalogue.AddFolderAsync(_volume.Id, tree);
        await _catalogue.Volumes.CopyFolderAsync(entry.Id, _volume.Id);
        var stray = await Awkward.OrphanTreeAsync(_catalogue);

        var asked = await RunAsync("rm", tree.Record("a.txt").Id.ToString(), stray.Record("stray.txt").Id.ToString());

        Assert.Contains("They go from the other folder listing the same scan too.",
            asked.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RemovingFromAScanOneFolderListsSaysNothingMore()
    {
        var tree = TestTree.Root("files").File("a.txt").Build();
        await _catalogue.AddFolderAsync(_volume.Id, tree);

        var asked = await RunAsync("rm", tree.Record("a.txt").Id.ToString());

        Assert.DoesNotContain("other folder", asked.Error.GetProperty("message").GetString());
    }

    #endregion

    #region compare

    [Fact]
    public async Task AFileReplacedByAFolderOfTheSameNameIsARemovedAndAnAddedRow()
    {
        var before = await _catalogue.AddFolderAsync(_volume.Id, TestTree.Root("a").File("notes", 5).Build());
        var after = await _catalogue.AddFolderAsync(_volume.Id, TestTree.Root("a").Folder("notes", n => n.File("x.txt", 1)).Build());

        var rows = (await RunAsync("compare", before.Id.ToString(), after.Id.ToString())).Json.GetProperty("rows")
            .EnumerateArray().Where(row => row.GetProperty("relativePath").GetString() == "notes").ToList();

        Assert.Equal(["added", "removed"], rows.Select(row => row.GetProperty("status").GetString()).Order());
    }

    [Fact]
    public async Task AFolderComparedWithItselfOrItsCopyHasNoDifferences()
    {
        var entry = await _catalogue.AddFolderAsync(_volume.Id, TestTree.Root("a").File("x.txt", 1).Build());
        var copy = await _catalogue.Volumes.CopyFolderAsync(entry.Id, _volume.Id);

        var self = await RunAsync("compare", entry.Id.ToString(), entry.Id.ToString(), "--exit-code");
        var copied = await RunAsync("compare", entry.Id.ToString(), copy.Id.ToString(), "--exit-code");

        Assert.Equal(0, self.ExitCode);
        Assert.Equal(0, copied.ExitCode);
        Assert.Equal(0, self.Json.GetProperty("rows").GetArrayLength());
    }

    #endregion
}
