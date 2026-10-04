using System.Diagnostics;
using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

public class DeepTreeTests
{
    private static async Task<(TempCatalogue Catalogue, RootFolderMetadata Entry, FolderTree Tree)> ChainAsync(int depth)
    {
        var catalogue = await TempCatalogue.CreateAsync();
        var volume = await catalogue.AddVolumeAsync("Backups");
        var tree = Awkward.Chain(depth);
        return (catalogue, await catalogue.AddFolderAsync(volume.Id, tree), tree);
    }

    private static string ExpectedPath(int depth) =>
        string.Join(Core.CataloguePath.Separator, new[] { "Backups:", "deep" }
            .Concat(Enumerable.Range(1, depth).Select(level => $"level{level}"))
            .Append("bottom.txt"));

    [Theory]
    [InlineData(40)]
    [InlineData(200)]
    [InlineData(1000)]
    public async Task ATreeDeeperThanJsonReadersNestStaysFlat(int depth)
    {
        var (catalogue, entry, _) = await ChainAsync(depth);
        using var _ = catalogue;

        var result = await CliRunner.RunAsync("tree", entry.Id.ToString(), "--db", catalogue.Path);

        Assert.Equal(0, result.ExitCode);

        var below = result.Json.GetProperty("below").EnumerateArray().ToList();
        Assert.Equal(depth + 1, below.Count);
        Assert.Equal(Enumerable.Range(1, depth + 1), below.Select(entry => entry.GetProperty("depth").GetInt32()));

        var bottom = below[^1].GetProperty("record");
        Assert.Equal("bottom.txt", bottom.GetProperty("name").GetString());
        Assert.Equal(ExpectedPath(depth),
            Assert.Single(bottom.GetProperty("entries").EnumerateArray()).GetProperty("cataloguePath").GetString());
    }

    [Fact]
    public async Task TheBottomOfADeepTreeIsReachedEveryOtherWay()
    {
        const int depth = 200;
        var (catalogue, _, tree) = await ChainAsync(depth);
        using var _ = catalogue;
        var bottom = tree.Record("bottom.txt");
        var expected = ExpectedPath(depth);

        static string PathOf(JsonElement record) =>
            Assert.Single(record.GetProperty("entries").EnumerateArray()).GetProperty("cataloguePath").GetString()!;

        var watch = Stopwatch.StartNew();

        var stat = await CliRunner.RunAsync("stat", bottom.Id.ToString(), "--db", catalogue.Path);
        var ls = await CliRunner.RunAsync("ls", tree.Record($"level{depth}").Id.ToString(), "--db", catalogue.Path);
        var search = await CliRunner.RunAsync("search", "bottom", "--db", catalogue.Path);

        Assert.Equal(expected, PathOf(stat.Json.GetProperty("record")));
        Assert.Equal(expected, PathOf(Assert.Single(ls.Json.GetProperty("children").EnumerateArray())));
        Assert.Equal(expected, PathOf(Assert.Single(search.Json.GetProperty("hits").EnumerateArray())));

        // A query per folder above: slow at this depth would be unusable at a real one
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"Took {watch.Elapsed}.");
    }
}
