using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

public class NameTests
{
    private static List<JsonElement> Children(CliResult ls) =>
        ls.Json.GetProperty("children").EnumerateArray().ToList();

    private static string PlacementOf(JsonElement record, string path) =>
        Assert.Single(record.GetProperty("entries").EnumerateArray()).GetProperty(path).GetString()!;

    private static async Task<(TempCatalogue Catalogue, DiskTree Disk, Guid EntryId)> ScannedAsync(
        Func<string[], Task<CliResult>> run)
    {
        var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var disk = new DiskTree();
        foreach (var name in Awkward.DiskNames)
        {
            disk.File(name, name.Length);
        }

        var scan = await run(["folder", "scan", disk.Root, "--volume", volume.Id.ToString(), "--db", catalogue.Path]);
        Assert.True(scan.ExitCode == 0, scan.Stdout);

        return (catalogue, disk, scan.Json.GetProperty("folder").GetProperty("id").GetGuid());
    }

    // In-process, and through a real process, where the console's encoding is the one in use
    public static IEnumerable<object[]> Runners() =>
    [
        [(Func<string[], Task<CliResult>>)(args => CliRunner.RunAsync(args)), "in-process"],
        [(Func<string[], Task<CliResult>>)(args => ProcessRunner.RunAsync(args)), "process"]
    ];

    [Theory]
    [MemberData(nameof(Runners))]
    public async Task NamesFromDiskComeBackExactly(Func<string[], Task<CliResult>> run, string how)
    {
        var (catalogue, disk, entryId) = await ScannedAsync(run);
        using var _ = catalogue;
        using var __ = disk;

        var ls = await run(["ls", entryId.ToString(), "--db", catalogue.Path]);
        var listed = Children(ls).ToDictionary(child => child.GetProperty("name").GetString()!);

        Assert.True(Awkward.DiskNames.Order().SequenceEqual(listed.Keys.Order()), $"{how}: {string.Join(", ", listed.Keys)}");

        foreach (var name in Awkward.DiskNames)
        {
            Assert.Equal(disk.PathOf(name), PlacementOf(listed[name], "physicalPath"));
            Assert.Equal($@"Backups:\{Path.GetFileName(disk.Root)}\{name}", PlacementOf(listed[name], "cataloguePath"));
        }
    }

    [Theory]
    [MemberData(nameof(Runners))]
    public async Task EveryNameCanBeSearchedForAndFoundAlone(Func<string[], Task<CliResult>> run, string how)
    {
        var (catalogue, disk, _) = await ScannedAsync(run);
        using var c = catalogue;
        using var d = disk;

        foreach (var name in Awkward.DiskNames)
        {
            var args = name.StartsWith('-')
                ? new[] { "search", "--db", catalogue.Path, "--", name }
                : ["search", name, "--db", catalogue.Path];

            var hits = (await run(args)).Json.GetProperty("hits").EnumerateArray()
                .Select(hit => hit.GetProperty("name").GetString())
                .ToList();

            Assert.True(hits.SequenceEqual([name]), $"{how}, '{name}': {string.Join(", ", hits)}");
        }
    }

    [Theory]
    [InlineData("CAFÉ", "café.txt")]
    [InlineData("ünïcödé àéîõü", "Ünïcödé ÀÉÎÕÜ.txt")]
    [InlineData("日本語", "日本語のファイル.txt")]
    public async Task CaseIsIgnoredBeyondAscii(string term, string name)
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        await catalogue.AddFolderAsync(volume.Id, Awkward.Tree(Awkward.Names));

        var result = await CliRunner.RunAsync("search", term, "--db", catalogue.Path);

        Assert.Contains(name, result.Json.GetProperty("hits").EnumerateArray().Select(hit => hit.GetProperty("name").GetString()));
    }

    // Catalogue data can hold what Windows won't put on disk: quotes, tabs, control characters
    [Fact]
    public async Task NamesNoDiskAllowsStillMakeValidJson()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        // Stored as a catalogue from before names were checked could hold it, to escape a '\' too
        var volume = new VirtualVolumeRecord { Id = Guid.NewGuid(), Name = "Back\"ups\\", Icon = "HardDrive" };
        await catalogue.Volumes.RestoreVirtualVolumeAsync(volume);
        string[] names = [.. Awkward.Names, "bell\u0007.txt", "nul\u0000.txt", "trailing dot.", "trailing space "];
        var entry = await catalogue.AddFolderAsync(volume.Id, Awkward.Tree(names));

        var result = await CliRunner.RunAsync("ls", entry.Id.ToString(), "--db", catalogue.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(names.Order(), Children(result).Select(child => child.GetProperty("name").GetString()!).Order());
    }

    public static TheoryData<string, string> NamingCommands => new()
    {
        { "volume create", @"C:\Backups" },
        { "volume update", "Backups:2" },
        { "folder scan", @"2024\summer" },
        { "folder update", "photos:old" },
        { "folder copy", @"2024\summer" }
    };

    // A ':' or '\' would read as a deeper path: a volume 'C:\Backups' heads 'C:\Backups:\summer'
    [Theory]
    [MemberData(nameof(NamingCommands))]
    public async Task ANameWithASeparatorIsRefusedBeforeAnythingIsWritten(string command, string name)
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var entry = await catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").File("a.jpg").Build());
        using var disk = new DiskTree().File("a.jpg", 1);
        var before = await catalogue.SnapshotAsync();

        string[] args = command switch
        {
            "volume create" => ["volume", "create", name],
            "volume update" => ["volume", "update", volume.Id.ToString(), "--name", name],
            "folder scan" => ["folder", "scan", disk.Root, "--volume", volume.Id.ToString(), "--label", name],
            "folder update" => ["folder", "update", entry.Id.ToString(), "--label", name],
            _ => ["folder", "copy", entry.Id.ToString(), "--to", volume.Id.ToString(), "--label", name]
        };
        var result = await CliRunner.RunAsync([.. args, "--db", catalogue.Path]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
        Assert.Contains("can't contain", result.Error.GetProperty("message").GetString());
        Assert.Equal(before, await catalogue.SnapshotAsync());
    }

    // Catalogues written before the rule keep their names, and read as they always did
    [Fact]
    public async Task SeparatorsAlreadyStoredStillRead()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = new VirtualVolumeRecord { Id = Guid.NewGuid(), Name = @"C:\Backups", Icon = "HardDrive" };
        await catalogue.Volumes.RestoreVirtualVolumeAsync(volume);
        var entry = await catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").File("a.jpg").Build());
        await catalogue.Volumes.UpdateFolderAsync(entry.Id, @"2024\summer", null, null, null);

        var stat = await CliRunner.RunAsync("stat", entry.Id.ToString(), "--db", catalogue.Path);

        Assert.Equal(@"C:\Backups:\2024\summer", PlacementOf(stat.Json.GetProperty("record"), "cataloguePath"));
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("  Holidays  ", "Holidays")]
    public async Task LabelsAreTrimmedAndBlankOnesCleared(string label, string? stored)
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var entry = await catalogue.AddFolderAsync(volume.Id, TestTree.Root("photos").Build());

        await CliRunner.RunAsync("folder", "update", entry.Id.ToString(), "--label", label, "--description", label, "--db", catalogue.Path);

        var saved = (await catalogue.Database.FindItemsAsync<RootFolderMetadata>(item => item.Id == entry.Id)).Single();
        Assert.Equal(stored, saved.Label);
        Assert.Equal(stored, saved.Description);
    }
}
