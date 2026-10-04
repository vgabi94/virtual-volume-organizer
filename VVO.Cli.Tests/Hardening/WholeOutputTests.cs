using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

[CollectionDefinition(nameof(LocalTimeZone), DisableParallelization = true)]
public sealed class LocalTimeZone;

/// <summary>
/// The whole of what a command writes, envelope and all, from a catalogue whose ids and times are
/// fixed. Each runs as it would on machines set up differently, and must come out the same to the
/// byte: an agent parsing it has no idea what culture or time zone the catalogue was read in.
/// </summary>
[Collection(nameof(LocalTimeZone))]
public class WholeOutputTests : IAsyncLifetime
{
    private TempCatalogue _catalogue = null!;
    private DiskTree _disk = null!;

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:000000000000}");

    private static readonly Guid Backups = Id(1);
    private static readonly Guid Archive = Id(2);
    private static readonly Guid Photos = Id(10);
    private static readonly Guid Listed = Id(20);
    private static readonly Guid Older = Id(30);

    // Ids handed out in the order the tree was built, from the given block on
    private static FolderTree Fixed(FolderTree tree, Guid entryId, int block)
    {
        var ids = new Dictionary<Guid, Guid>();
        Guid Map(Guid id) => ids.TryGetValue(id, out var mapped) ? mapped : ids[id] = Id(block + ids.Count);

        var records = tree.Records
            .Select(record => record with
            {
                Id = Map(record.Id),
                RootFolderId = Map(record.RootFolderId),
                ParentId = record.ParentId is { } parentId ? Map(parentId) : null
            })
            .ToList();

        return new FolderTree(tree.Metadata with { Id = entryId, TreeId = Map(tree.Metadata.TreeId) }, records);
    }

    private static readonly DateTime Earlier = new(2025, 6, 30, 23, 30, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        _disk = new DiskTree()
            .File("readme.txt", 10, TestTree.DefaultTime)
            .File(@"photos\beach.jpg", 3000, TestTree.DefaultTime.AddHours(1))
            .File(@"photos\new.jpg", 5, TestTree.DefaultTime);

        var current = Fixed(TestTree.Root("Photos", _disk.Root)
            .File("readme.txt", 10)
            .File("old notes.txt", 1)
            .Folder("photos", photos => photos
                .File("beach.jpg", 2048)
                .Folder("trip", trip => trip.File("day1.jpg", 4096)))
            .Build(), Photos, 100);

        var older = Fixed(TestTree.Root("Photos", @"E:\Photos")
            .File("readme.txt", 8, Earlier)
            .Folder("photos", photos => photos.File("beach.jpg", 2048))
            .Build(), Older, 200);

        var database = _catalogue.Database;
        await database.InsertItemsAsync<VirtualVolumeRecord>([
            new() { Id = Backups, Name = "Backups", Icon = "HardDrive", Color = "#FF8800" },
            new() { Id = Archive, Name = "Archive", Icon = "Archive" },
            // Turkish lowers I to a dotless ı, which sorts before i: these two swap places there
            new() { Id = Id(3), Name = "ice", Icon = "HardDrive" },
            new() { Id = Id(4), Name = "Ivy", Icon = "HardDrive" }
        ]);
        await database.InsertItemsAsync<RootFolderMetadata>([
            current.Metadata with { VirtualVolumeId = Backups, Label = "Holidays", Description = "Summer, mostly" },
            current.Metadata with { Id = Listed, VirtualVolumeId = Archive, Label = "ice" },
            older.Metadata with { VirtualVolumeId = Archive, Label = "Ivy", LastScanned = Earlier }
        ]);
        await database.InsertItemsAsync<FileRecord>([.. current.Records, .. older.Records]);
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        _disk.Dispose();
        return Task.CompletedTask;
    }

    private static Guid Record(int n) => Id(100 + n);

    public static TheoryData<string, string> Machines => new()
    {
        { "en-US", "UTC" },
        { "de-DE", "Tokyo" },
        { "tr-TR", "Kathmandu" }
    };

    private async Task AssertMatchesAsync(string name, string culture, string zone, string[] args, ScriptedTerminal? terminal = null)
    {
        var result = await AsIfOn(culture, zone, () => CliRunner.RunAsync([.. args, "--db", _catalogue.Path], terminal: terminal));

        Snapshot.AssertMatches(Anonymised(result.Stdout), Path.Combine("Commands", name));
    }

    // The temp folders, and what the file system says about the catalogue file, are all that
    // changes from run to run
    private string Anonymised(string output)
    {
        output = Regex.Replace(output, @"""fileSize"": \d+", @"""fileSize"": ""<bytes>""");
        output = Regex.Replace(output, @"""lastWrite"": ""[^""]+""", @"""lastWrite"": ""<time>""");

        foreach (var (path, placeholder) in new[] { (_catalogue.Path, "<catalogue>"), (_disk.Root, "<disk>") })
        {
            output = output.Replace(path.Replace(@"\", @"\\"), placeholder).Replace(path, placeholder);
        }

        return output;
    }

    #region Reading

    [Theory, MemberData(nameof(Machines))]
    public Task DbInfo(string culture, string zone) =>
        AssertMatchesAsync("db-info.json", culture, zone, ["db", "info"]);

    [Theory, MemberData(nameof(Machines))]
    public Task VolumeList(string culture, string zone) =>
        AssertMatchesAsync("volume-list.json", culture, zone, ["volume", "list"]);

    [Theory, MemberData(nameof(Machines))]
    public Task FolderList(string culture, string zone) =>
        AssertMatchesAsync("folder-list.json", culture, zone, ["folder", "list"]);

    [Theory, MemberData(nameof(Machines))]
    public Task LsOfAnEntry(string culture, string zone) =>
        AssertMatchesAsync("ls-entry.json", culture, zone, ["ls", Photos.ToString()]);

    [Theory, MemberData(nameof(Machines))]
    public Task LsOfAFolderInASharedTree(string culture, string zone) =>
        AssertMatchesAsync("ls-folder.json", culture, zone, ["ls", Record(3).ToString()]);

    [Theory, MemberData(nameof(Machines))]
    public Task LsAsATable(string culture, string zone) =>
        AssertMatchesAsync("ls-entry.txt", culture, zone, ["ls", Photos.ToString(), "--format", "table"]);

    [Theory, MemberData(nameof(Machines))]
    public Task Stat(string culture, string zone) =>
        AssertMatchesAsync("stat.json", culture, zone, ["stat", Record(4).ToString()]);

    [Theory, MemberData(nameof(Machines))]
    public Task Search(string culture, string zone) =>
        AssertMatchesAsync("search.json", culture, zone, ["search", "JPG"]);

    [Theory, MemberData(nameof(Machines))]
    public Task Compare(string culture, string zone) =>
        AssertMatchesAsync("compare.json", culture, zone, ["compare", Older.ToString(), Photos.ToString()]);

    #endregion

    #region Writing

    [Theory, MemberData(nameof(Machines))]
    public Task RescanAskingFirst(string culture, string zone) =>
        AssertMatchesAsync("rescan-confirmation-required.json", culture, zone, ["folder", "rescan", Photos.ToString()]);

    [Theory, MemberData(nameof(Machines))]
    public Task Add(string culture, string zone) =>
        AssertMatchesAsync("add.json", culture, zone, ["add", Record(3).ToString(), _disk.PathOf(@"photos\new.jpg"), _disk.PathOf("photos")]);

    [Theory, MemberData(nameof(Machines))]
    public Task Rm(string culture, string zone) =>
        AssertMatchesAsync("rm.json", culture, zone, ["rm", Record(2).ToString(), Record(5).ToString()], ScriptedTerminal.Typing("delete"));

    #endregion

    #region Another machine

    /// <summary>
    /// Runs the command under another culture, which flows with it, and another local time zone,
    /// which is the process's own: hence the collection these tests run alone in.
    /// </summary>
    private static async Task<T> AsIfOn<T>(string culture, string zone, Func<Task<T>> run)
    {
        var (savedCulture, savedUiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        SetLocalTimeZone(zone switch
        {
            // Not TimeZoneInfo.Utc itself: as the local zone it makes every conversion between
            // the two a no-op, which no real machine's local zone does
            "UTC" => TimeZoneInfo.CreateCustomTimeZone("UTC", TimeSpan.Zero, "UTC", "UTC"),
            "Tokyo" => TimeZoneInfo.CreateCustomTimeZone("Tokyo", TimeSpan.FromHours(9), "Tokyo", "Tokyo"),
            _ => TimeZoneInfo.CreateCustomTimeZone(zone, new TimeSpan(5, 45, 0), zone, zone)
        });

        try
        {
            return await run();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (savedCulture, savedUiCulture);
            TimeZoneInfo.ClearCachedData();
        }
    }

    // The runtime offers no way to change the local zone of a running process
    private static void SetLocalTimeZone(TimeZoneInfo zone)
    {
        TimeZoneInfo.ClearCachedData();
        var cachedData = typeof(TimeZoneInfo).GetField("s_cachedData", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        cachedData.GetType().GetField("_localTimeZone", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cachedData, zone);

        Assert.Equal(zone.BaseUtcOffset, TimeZoneInfo.Local.BaseUtcOffset);
    }

    #endregion
}
