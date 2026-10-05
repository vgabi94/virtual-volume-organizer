using System.Diagnostics;
using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

/// <summary>
/// Comparing from a folder inside a tree, against the catalogues and folders real use brings:
/// damage around it, deep chains, awkward names, trees listed twice or not at all.
/// </summary>
public class NestedCompareTests : IAsyncLifetime
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

    private Task<CliResult> CompareAsync(params object[] args) =>
        CliRunner.RunAsync(["compare", .. args.Select(arg => arg.ToString()!), "--db", _catalogue.Path]);

    private Task<RootFolderMetadata> AddAsync(FolderTree tree) => _catalogue.AddFolderAsync(_volume.Id, tree);

    private static List<string?> Paths(CliResult result) =>
        result.Json.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("relativePath").GetString()).ToList();

    private static JsonElement Side(CliResult result, string side) => result.Json.GetProperty(side);

    #region Around damage

    [Fact]
    public async Task EveryKindOfDamageElsewhereLeavesASubfolderComparable()
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var catalogue = fixture.Catalogue;
        await Awkward.EntryWithoutTreeAsync(catalogue, fixture.Backups.Id);
        await Awkward.RecordWithoutParentAsync(catalogue, fixture.Backups.Id);
        await Awkward.EntryInMissingVolumeAsync(catalogue);
        await Awkward.OrphanTreeAsync(catalogue);
        await Awkward.ParentLoopAsync(catalogue, fixture.Backups.Id);

        var result = await CliRunner.RunAsync(
            ["compare", fixture.SubFolder.Id.ToString(), "--disk", fixture.Disk.PathOf("photos"), "--exit-code", "--db", catalogue.Path]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(Paths(result));
    }

    // Only the folders above the one compared have to be whole, not every folder in its tree
    [Fact]
    public async Task DamageBesideTheComparedFolderIsNoConcernOfIt()
    {
        var tree = TestTree.Root("r")
            .Folder("kept", kept => kept.File("a.txt", 1))
            .Folder("gone", gone => gone.Folder("orphan", orphan => orphan.File("b.txt", 1)))
            .Build();
        var entry = await AddAsync(tree);
        await _catalogue.Database.RemoveItemsAsync<FileRecord>([tree.Record("gone").Id]);

        var result = await CompareAsync(tree.Record("kept").Id, tree.Record("kept").Id, "--exit-code");

        Assert.Equal(0, result.ExitCode);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    [Fact]
    public async Task ADamagedFolderOnTheRightIsReportedAsOnTheLeft()
    {
        var (looped, loopTree) = await Awkward.ParentLoopAsync(_catalogue, _volume.Id);

        var tree = TestTree.Root("broken").Folder("gone", gone => gone.Folder("kept", kept => kept.File("a.txt", 1))).Build();
        await AddAsync(tree);
        await _catalogue.Database.RemoveItemsAsync<FileRecord>([tree.Record("gone").Id]);

        var loop = await CompareAsync(looped.Id, loopTree.Record("b").Id);
        var lost = await CompareAsync(looped.Id, tree.Record("kept").Id);

        Assert.Equal(("error", 1), (loop.ErrorCode, loop.ExitCode));
        Assert.Equal(("error", 1), (lost.ErrorCode, lost.ExitCode));
    }

    [Fact]
    public async Task AReadOnlyCatalogueComparesSubfoldersAndStaysAsItWas()
    {
        using var fixture = await MatrixFixture.CreateAsync();
        fixture.Catalogue.MakeReadOnly();
        var before = File.ReadAllBytes(fixture.Catalogue.Path);

        var result = await CliRunner.RunAsync(
            ["compare", fixture.SubFolder.Id.ToString(), fixture.Copy.Id.ToString(), "--db", fixture.Catalogue.Path]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(fixture.Catalogue.Path));
    }

    [Fact]
    public async Task ComparingSubfoldersWritesNothing()
    {
        var tree = TestTree.Root("r").Folder("a", a => a.File("x.txt", 1)).Folder("b", b => b.File("y.txt", 1)).Build();
        await AddAsync(tree);
        var before = await _catalogue.SnapshotAsync();

        await CompareAsync(tree.Record("a").Id, tree.Record("b").Id);

        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    #endregion

    #region Where the folders are

    [Fact]
    public async Task AFolderComparedWithItselfHasNoDifferences()
    {
        var tree = TestTree.Root("r").Folder("lib", lib => lib.File("a.cs", 1).Folder("deep", deep => deep.File("b.cs", 2))).Build();
        await AddAsync(tree);

        var result = await CompareAsync(tree.Record("lib").Id, tree.Record("lib").Id, "--exit-code");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(Paths(result));
    }

    // One side holds the other: what is beside the inner folder reads as missing from it
    [Fact]
    public async Task AFolderComparedWithTheFolderHoldingItSeesItselfAdded()
    {
        var tree = TestTree.Root("r").File("a.txt", 1).Folder("lib", lib => lib.File("a.txt", 1)).Build();
        var entry = await AddAsync(tree);

        var result = await CompareAsync(tree.Record("lib").Id, entry.Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["", "lib"], Paths(result));
        Assert.Equal("added", result.Json.GetProperty("rows")[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task TwoSiblingsAreComparedBelowTheirOwnNames()
    {
        var tree = TestTree.Root("r")
            .Folder("v1", v1 => v1.File("same.txt", 1).File("old.txt", 1))
            .Folder("v2", v2 => v2.File("same.txt", 1).File("new.txt", 1))
            .Build();
        await AddAsync(tree);

        var result = await CompareAsync(tree.Record("v1").Id, tree.Record("v2").Id, "--include-unchanged");

        Assert.Equal(["", "new.txt", "old.txt", "same.txt"], Paths(result));
        Assert.Equal("v1", result.Json.GetProperty("rows")[0].GetProperty("left").GetProperty("name").GetString());
        Assert.Equal("v2", result.Json.GetProperty("rows")[0].GetProperty("right").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExitCodeOneComesFromASubfolderThatDiffers()
    {
        var tree = TestTree.Root("r").Folder("v1", v1 => v1.File("a.txt", 1)).Folder("v2", v2 => v2.File("a.txt", 2)).Build();
        await AddAsync(tree);

        var result = await CompareAsync(tree.Record("v1").Id, tree.Record("v2").Id, "--exit-code");

        Assert.Equal(1, result.ExitCode);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ASubfolderReadsTheSameAsATable()
    {
        var tree = TestTree.Root("r").Folder("v1", v1 => v1.File("a.txt", 1)).Folder("v2", v2 => v2.File("a.txt", 2)).Build();
        await AddAsync(tree);

        var result = await CompareAsync(tree.Record("v1").Id, tree.Record("v2").Id, "--format", "table");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Stdout.Split('\n'), line => line.StartsWith("changed") && line.Contains("a.txt"));
        Assert.DoesNotContain("v1", result.Stdout);
    }

    #endregion

    #region How each side is described

    // Named by its record, the top of a tree is a folder like any other: its own name, no entry
    [Fact]
    public async Task TheTopOfATreeNamedByItsRecordIsDescribedByItsScannedName()
    {
        var tree = TestTree.Root("Photos", path: @"E:\Photos").File("a.jpg", 1).Build();
        var entry = await AddAsync(tree);
        await _catalogue.Volumes.UpdateFolderAsync(entry.Id, "Holidays", null, null, null);

        var result = await CompareAsync(entry.TreeId, entry.Id);

        Assert.Equal(JsonValueKind.Null, Side(result, "left").GetProperty("entryId").ValueKind);
        Assert.Equal("Photos", Side(result, "left").GetProperty("title").GetString());
        Assert.Equal(@"E:\Photos", Side(result, "left").GetProperty("path").GetString());
        Assert.Equal("Holidays", Side(result, "right").GetProperty("title").GetString());
        Assert.Empty(Paths(result));
    }

    [Fact]
    public async Task AFolderInATreeNoEntryListsHasNoPath()
    {
        var orphan = await Awkward.OrphanTreeAsync(_catalogue);

        var result = await CompareAsync(orphan.Metadata.TreeId, orphan.Metadata.TreeId);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(JsonValueKind.Null, Side(result, "left").GetProperty("path").ValueKind);
        Assert.Equal("orphan", Side(result, "left").GetProperty("title").GetString());
    }

    // The sidebar leaves out an entry whose volume is gone, so it places nothing
    [Fact]
    public async Task AFolderListedOnlyInAVolumeThatIsGoneHasNoPath()
    {
        var stranded = await Awkward.EntryInMissingVolumeAsync(_catalogue);

        var result = await CompareAsync(stranded.TreeId, stranded.TreeId);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(JsonValueKind.Null, Side(result, "left").GetProperty("path").ValueKind);
    }

    [Fact]
    public async Task AFolderInATreeListedWithoutAPathOnDiskHasNoPath()
    {
        var tree = TestTree.Root("r", path: string.Empty).Folder("lib", lib => lib.File("a.txt", 1)).Build();
        await AddAsync(tree);

        var result = await CompareAsync(tree.Record("lib").Id, tree.Record("lib").Id);

        Assert.True(result.ExitCode == 0, result.Stdout);
        Assert.Equal(JsonValueKind.Null, Side(result, "left").GetProperty("path").ValueKind);
    }

    [Fact]
    public async Task TheDiskSideOfASubfolderComparisonHasNoIds()
    {
        using var disk = new DiskTree().File(@"sub\a.txt", 1);
        var scan = await new Core.Services.FileScannerService().ScanDirectoryAsync(disk.Root);
        await _catalogue.Volumes.AddFolderAsync(_volume.Id, scan.Metadata, scan.Records.ToList());
        var sub = scan.Records.Single(record => record.Name == "sub");

        var result = await CompareAsync(sub.Id, "--disk", disk.PathOf("sub"));

        Assert.Equal(JsonValueKind.Null, Side(result, "right").GetProperty("entryId").ValueKind);
        Assert.Equal(JsonValueKind.Null, Side(result, "right").GetProperty("recordId").ValueKind);
        Assert.Equal(disk.PathOf("sub"), Side(result, "left").GetProperty("path").GetString());
    }

    #endregion

    #region Mistakes

    [Fact]
    public async Task AnUnknownIdOnEitherSideIsNotFoundAndSaysWhatWasLookedFor()
    {
        var tree = TestTree.Root("r").Folder("lib", lib => lib.File("a.txt", 1)).Build();
        await AddAsync(tree);
        var unknown = Guid.NewGuid();

        var left = await CompareAsync(unknown, tree.Record("lib").Id);
        var right = await CompareAsync(tree.Record("lib").Id, unknown);

        Assert.Equal(("not_found", 3), (left.ErrorCode, left.ExitCode));
        Assert.Equal(("not_found", 3), (right.ErrorCode, right.ExitCode));
        Assert.Contains("folder entry or catalogue record", right.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AFileOnTheLeftIsAUsageErrorNamingIt()
    {
        var tree = TestTree.Root("r").File("notes.txt", 1).Build();
        var entry = await AddAsync(tree);

        var result = await CompareAsync(tree.Record("notes.txt").Id, entry.Id);

        Assert.Equal(("usage", 2), (result.ErrorCode, result.ExitCode));
        Assert.Contains("notes.txt", result.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AFileAgainstDiskIsAUsageErrorBeforeTheDiskIsRead()
    {
        var tree = TestTree.Root("r").File("notes.txt", 1).Build();
        await AddAsync(tree);

        var result = await CompareAsync(tree.Record("notes.txt").Id, "--disk", Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}"));

        Assert.Equal("usage", result.ErrorCode);
    }

    #endregion

    #region Shapes

    [Fact]
    public async Task ADeepSubfolderIsComparedFromWhereItIs()
    {
        const int depth = 1000;
        var older = Awkward.Chain(depth, "older");
        var newer = Awkward.Chain(depth, "newer");
        await AddAsync(older);
        await AddAsync(newer);
        await _catalogue.Database.UpdateItemsAsync([newer.Record("bottom.txt") with { Size = 2 }]);

        var watch = Stopwatch.StartNew();
        var result = await CompareAsync(older.Record("level500").Id, newer.Record("level500").Id);

        Assert.Equal(0, result.ExitCode);
        var expected = string.Join('\\', Enumerable.Range(501, depth - 500).Select(level => $"level{level}").Append("bottom.txt"));
        Assert.Equal(expected, Paths(result)[^1]);
        Assert.Equal(1, result.Json.GetProperty("counts").GetProperty("changed").GetInt32());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public async Task AwkwardNamesBelowASubfolderComeOutAsTheyAre()
    {
        var older = TestTree.Root("r").Folder("日本語 🎉 [x]", folder => Add(folder, Awkward.Names)).Build();
        var newer = TestTree.Root("r").Folder("日本語 🎉 [x]", folder => Add(folder, Awkward.Names.Skip(1))).Build();
        await AddAsync(older);
        await AddAsync(newer);

        var result = await CompareAsync(older.Record("日本語 🎉 [x]").Id, newer.Record("日本語 🎉 [x]").Id);

        Assert.Equal(["", Awkward.Names[0]], Paths(result));

        static void Add(TreeBuilder folder, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                folder.File(name, name.Length);
            }
        }
    }

    // A tree is read whole whichever folder in it is compared, so a wide one has to stay quick
    [Fact]
    public async Task ASmallFolderInAWideTreeIsComparedQuickly()
    {
        var builder = TestTree.Root("wide").Folder("small", small => small.File("a.txt", 1));
        for (var i = 0; i < 20_000; i++)
        {
            builder.File($"file{i:00000}.txt", i);
        }

        var tree = builder.Build();
        await AddAsync(tree);

        var watch = Stopwatch.StartNew();
        var result = await CompareAsync(tree.Record("small").Id, tree.Record("small").Id, "--exit-code");

        Assert.Equal(0, result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"Took {watch.Elapsed}.");
    }

    #endregion
}
