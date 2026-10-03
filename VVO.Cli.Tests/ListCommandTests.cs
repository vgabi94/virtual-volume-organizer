using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class ListCommandTests
{
    private static FolderTree Photos() => TestTree.Root("photos", path: @"D:\photos")
        .File("a.jpg", 10)
        .Folder("2024", year => year.File("b.jpg", 20))
        .Build();

    #region volume list

    [Fact]
    public async Task AnEmptyCatalogueHasNoVolumes()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        var result = await CliRunner.RunAsync("volume", "list", "--db", catalogue.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Json.GetArrayLength());
    }

    [Fact]
    public async Task VolumesAreListedByNameWithHowManyFoldersEachHolds()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var music = await catalogue.AddVolumeAsync("music", "HardDisk", "#112233");
        var backups = await catalogue.AddVolumeAsync("Backups");
        await catalogue.AddFolderAsync(backups.Id, Photos());
        await catalogue.AddFolderAsync(backups.Id, TestTree.Root("docs").Build());

        var volumes = (await CliRunner.RunAsync("volume", "list", "--db", catalogue.Path)).Json;

        Assert.Equal(2, volumes.GetArrayLength());

        var first = volumes[0];
        Assert.Equal(backups.Id, first.GetProperty("id").GetGuid());
        Assert.Equal("Backups", first.GetProperty("name").GetString());
        Assert.Equal(2, first.GetProperty("folderCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("color").ValueKind);

        var second = volumes[1];
        Assert.Equal(music.Id, second.GetProperty("id").GetGuid());
        Assert.Equal("HardDisk", second.GetProperty("icon").GetString());
        Assert.Equal("#112233", second.GetProperty("color").GetString());
        Assert.Equal(0, second.GetProperty("folderCount").GetInt32());
    }

    #endregion

    #region folder list

    [Fact]
    public async Task FoldersAcrossEveryVolumeAreListed()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var backups = await catalogue.AddVolumeAsync("Backups");
        var archive = await catalogue.AddVolumeAsync("Archive");
        await catalogue.AddFolderAsync(backups.Id, Photos());
        await catalogue.AddFolderAsync(archive.Id, TestTree.Root("docs").Build());

        var folders = (await CliRunner.RunAsync("folder", "list", "--db", catalogue.Path)).Json;

        Assert.Equal(2, folders.GetArrayLength());
    }

    [Fact]
    public async Task AFolderReportsWhatTheSidebarShowsAndWhatItHolds()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var backups = await catalogue.AddVolumeAsync("Backups");
        var entry = await catalogue.AddFolderAsync(backups.Id, Photos());

        var folder = Assert.Single(
            (await CliRunner.RunAsync("folder", "list", "--db", catalogue.Path)).Json.EnumerateArray());

        Assert.Equal(entry.Id, folder.GetProperty("id").GetGuid());
        Assert.Equal(backups.Id, folder.GetProperty("volumeId").GetGuid());
        Assert.Equal(entry.TreeId, folder.GetProperty("treeId").GetGuid());
        Assert.Equal("photos", folder.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, folder.GetProperty("label").ValueKind);
        Assert.Equal(@"D:\photos", folder.GetProperty("path").GetString());
        Assert.Equal(30, folder.GetProperty("size").GetInt64());
        Assert.Equal(3, folder.GetProperty("recordCount").GetInt32());
    }

    [Theory]
    [InlineData(null, "photos")]
    [InlineData("  ", "photos")]
    [InlineData("Holidays", "Holidays")]
    public async Task TheTitleIsTheLabelOrElseTheScannedName(string? label, string title)
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var backups = await catalogue.AddVolumeAsync("Backups");
        var entry = await catalogue.AddFolderAsync(backups.Id, Photos());
        await catalogue.Volumes.UpdateFolderAsync(entry.Id, label, "Summer", "FolderImage", "#3366CC");

        var folder = (await CliRunner.RunAsync("folder", "list", "--db", catalogue.Path)).Json[0];

        Assert.Equal(title, folder.GetProperty("title").GetString());
        Assert.Equal("Summer", folder.GetProperty("description").GetString());
        Assert.Equal("FolderImage", folder.GetProperty("icon").GetString());
        Assert.Equal("#3366CC", folder.GetProperty("color").GetString());
    }

    [Fact]
    public async Task VolumeLimitsTheListToOneVolume()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var backups = await catalogue.AddVolumeAsync("Backups");
        var archive = await catalogue.AddVolumeAsync("Archive");
        await catalogue.AddFolderAsync(backups.Id, Photos());
        var docs = await catalogue.AddFolderAsync(archive.Id, TestTree.Root("docs").Build());

        var folders = (await CliRunner.RunAsync(
            "folder", "list", "--db", catalogue.Path, "--volume", archive.Id.ToString())).Json;

        Assert.Equal(docs.Id, Assert.Single(folders.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AnEmptyVolumeListsNothing()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var empty = await catalogue.AddVolumeAsync("Empty");

        var result = await CliRunner.RunAsync("folder", "list", "--db", catalogue.Path, "--volume", empty.Id.ToString());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Json.GetArrayLength());
    }

    [Fact]
    public async Task ACopyIsListedApartFromItsOriginalOnTheSameTree()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        var backups = await catalogue.AddVolumeAsync("Backups");
        var original = await catalogue.AddFolderAsync(backups.Id, Photos());
        var copy = await catalogue.Volumes.CopyFolderAsync(original.Id, backups.Id, "Photos (copy)");

        var folders = (await CliRunner.RunAsync("folder", "list", "--db", catalogue.Path)).Json;

        Assert.Equal(2, folders.GetArrayLength());
        Assert.All(folders.EnumerateArray(), folder =>
        {
            Assert.Equal(original.TreeId, folder.GetProperty("treeId").GetGuid());
            Assert.Equal(3, folder.GetProperty("recordCount").GetInt32());
        });
        Assert.Contains(folders.EnumerateArray(), folder => folder.GetProperty("id").GetGuid() == copy.Id);
    }

    [Fact]
    public async Task AnUnknownVolumeIsNotFound()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        var result = await CliRunner.RunAsync(
            "folder", "list", "--db", catalogue.Path, "--volume", Guid.NewGuid().ToString());

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }

    [Fact]
    public async Task AVolumeIdThatIsNotAGuidIsAUsageError()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        var result = await CliRunner.RunAsync("folder", "list", "--db", catalogue.Path, "--volume", "Backups");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    #endregion
}
