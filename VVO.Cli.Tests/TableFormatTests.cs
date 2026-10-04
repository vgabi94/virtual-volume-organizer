using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class TableFormatTests : IAsyncLifetime
{
    private static readonly FolderTree Photos = TestTree.Root("photos", path: @"D:\photos")
        .File("beach.jpg", 2048)
        .Folder("2024", year => year.File("beach-2.jpg", 10))
        .Build();

    private TempCatalogue _catalogue = null!;
    private RootFolderMetadata _photos = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        var volume = await _catalogue.AddVolumeAsync("Backups");
        _photos = await _catalogue.AddFolderAsync(volume.Id, Photos);
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private Task<CliResult> TableAsync(params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path, "--format", "table"]);

    private static string[] Lines(CliResult result) =>
        result.Stdout.TrimEnd().Split(Environment.NewLine);

    [Fact]
    public async Task VolumesAreATable()
    {
        var lines = Lines(await TableAsync("volume", "list"));

        Assert.StartsWith("ID", lines[0]);
        Assert.Contains("NAME", lines[0]);
        Assert.Contains("FOLDERS", lines[0]);
        Assert.Contains("Backups", lines[1]);
        Assert.EndsWith("1", lines[1]);
    }

    [Fact]
    public async Task ColumnsLineUp()
    {
        await _catalogue.AddVolumeAsync("A much longer volume name");

        var lines = Lines(await TableAsync("volume", "list"));
        var iconColumn = lines[0].IndexOf("ICON", StringComparison.Ordinal);

        Assert.All(lines.Skip(1), line => Assert.Equal("HardDrive", line.Substring(iconColumn, "HardDrive".Length)));
    }

    [Fact]
    public async Task FoldersAreATableWithReadableSizes()
    {
        var lines = Lines(await TableAsync("folder", "list"));

        Assert.Contains("TITLE", lines[0]);
        Assert.Contains("photos", lines[1]);
        Assert.Contains("2 KB", lines[1]);
        Assert.Contains(@"D:\photos", lines[1]);
    }

    [Fact]
    public async Task AListingIsHeadedByWhereItIs()
    {
        var lines = Lines(await TableAsync("ls", Photos.Record("2024").Id.ToString()));

        Assert.Equal(@"Backups:\photos\2024", lines[0]);
        Assert.StartsWith("TYPE", lines[1]);
        Assert.Contains("beach-2.jpg", lines[2]);
    }

    [Fact]
    public async Task ATreeIsIndentedByDepth()
    {
        var lines = Lines(await TableAsync("tree", _photos.Id.ToString()));

        Assert.Equal(@"Backups:\photos", lines[0]);
        Assert.Equal(["  2024\\", "    beach-2.jpg", "  beach.jpg"], lines[1..]);
    }

    [Fact]
    public async Task ACutSearchSaysHowManyThereWere()
    {
        var lines = Lines(await TableAsync("search", "beach", "--limit", "1"));

        Assert.Contains("PATH", lines[0]);
        Assert.Equal("1 of 2 hits shown.", lines[^1]);
    }

    [Fact]
    public async Task AComparisonIsHeadedByItsCounts()
    {
        var other = await _catalogue.AddFolderAsync(
            _photos.VirtualVolumeId, TestTree.Root("photos").File("beach.jpg", 2048).Build());

        var lines = Lines(await TableAsync("compare", _photos.Id.ToString(), other.Id.ToString()));

        Assert.Equal("0 added, 1 removed, 0 changed", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("removed") && line.Contains("2024") && line.Contains("1 inside"));
    }

    [Fact]
    public async Task NothingToListSaysSo()
    {
        var lines = Lines(await TableAsync("search", "zebra"));

        Assert.Equal(["Nothing to list."], lines);
    }

    [Fact]
    public async Task OtherResultsAreSummarisedAValueALine()
    {
        var lines = Lines(await TableAsync("db", "info"));

        Assert.Contains($"path: {_catalogue.Path}", lines);
        Assert.Contains("volumeCount: 1", lines);
        Assert.Contains("recordCount: 3", lines);
    }

    [Fact]
    public async Task ErrorsStayJson()
    {
        var result = await TableAsync("ls", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public async Task TheFormatIsReadWithoutRegardToCase()
    {
        var result = await CliRunner.RunAsync("volume", "list", "--db", _catalogue.Path, "--format", "TABLE");

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("ID", result.Stdout);
    }

    [Fact]
    public async Task AnUnknownFormatIsAUsageError()
    {
        var result = await CliRunner.RunAsync("volume", "list", "--db", _catalogue.Path, "--format", "xml");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task JsonIsTheDefault()
    {
        var result = await CliRunner.RunAsync("volume", "list", "--db", _catalogue.Path);

        Assert.Equal(1, result.Json.GetArrayLength());
    }
}
