using Avalonia.Headless.XUnit;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// Adding catalogued files and folders into the folder the volume explorer is showing.
/// </summary>
public class ExplorerAddTests : UiTestBase
{
    private async Task<(VolumeExplorerViewModel Explorer, RootFolderMetadata Entry)> GivenAFolderShownAsync()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));

        return (explorer, entry);
    }

    [AvaloniaFact]
    public async Task AddingAFolderPutsItInTheOpenFolder()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        var photos = Path.Combine(TempDirectory(), "Photos");
        Directory.CreateDirectory(photos);
        File.WriteAllBytes(Path.Combine(photos, "shot.jpg"), new byte[15]);
        PickFolders(photos);

        await explorer.AddFoldersCommand.ExecuteAsync(null);

        Assert.Contains(explorer.Files!, file => file.Name == "Photos");
        Assert.Contains("3 items", explorer.ItemSummary);
        Assert.Contains("45 B", explorer.ItemSummary);
    }

    [AvaloniaFact]
    public async Task AddingAFilePutsItInTheOpenFolder()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        var extra = Path.Combine(TempDirectory(), "extra.txt");
        File.WriteAllBytes(extra, new byte[5]);
        PickDiskFiles(extra);

        await explorer.AddFilesCommand.ExecuteAsync(null);

        Assert.Contains(explorer.Files!, file => file.Name == "extra");
        Assert.Contains("3 items", explorer.ItemSummary);
        Assert.Contains("35 B", explorer.ItemSummary);
    }

    [AvaloniaFact]
    public async Task ANameAlreadyThereIsSkippedAndSaidSo()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        var extra = Path.Combine(TempDirectory(), "readme.md");
        File.WriteAllBytes(extra, new byte[5]);
        PickDiskFiles(extra);

        await explorer.AddFilesCommand.ExecuteAsync(null);

        Assert.DoesNotContain(explorer.Files!, file => file.Size == "5 B" && file.Name == "readme");
        Assert.Contains(Told, message => message.Title == "Some items were not added");
        Assert.Contains("2 items · 30 B", explorer.ItemSummary);
    }

    [AvaloniaFact]
    public async Task CancellingThePickerAddsNothing()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        PickFolders();
        PickDiskFiles();

        await explorer.AddFoldersCommand.ExecuteAsync(null);
        await explorer.AddFilesCommand.ExecuteAsync(null);

        Assert.Equal(2, explorer.Files!.Count);
    }

    [AvaloniaFact]
    public async Task AddingAFileUpdatesTheSidebarSize()
    {
        var sidebar = NewSidebar();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddFolderAsync(volume.Id, "Code");
        await sidebar.LoadAsync();

        var explorer = NewExplorer();
        Detach(explorer);
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));
        var extra = Path.Combine(TempDirectory(), "extra.txt");
        File.WriteAllBytes(extra, new byte[5]);
        PickDiskFiles(extra);

        await explorer.AddFilesCommand.ExecuteAsync(null);

        Assert.Equal("35 B", sidebar.VirtualVolumes.Single().Folders.Single().Subtitle);
    }

    [AvaloniaFact]
    public async Task AddingUpdatesEverySidebarRowSharingTheTree()
    {
        var sidebar = NewSidebar();
        var one = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await Volumes.CreateVirtualVolumeAsync("copy", "CompactDisc");
        var entry = await AddFolderAsync(one.Id, "Code");
        await Volumes.CopyFolderAsync(entry.Id, two.Id);
        await sidebar.LoadAsync();

        var explorer = NewExplorer();
        Detach(explorer);
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));
        var extra = Path.Combine(TempDirectory(), "extra.txt");
        File.WriteAllBytes(extra, new byte[5]);
        PickDiskFiles(extra);

        await explorer.AddFilesCommand.ExecuteAsync(null);

        Assert.Equal(
            ["35 B", "35 B"],
            sidebar.VirtualVolumes.SelectMany(node => node.Folders).Select(folder => folder.Subtitle));
    }

    [AvaloniaFact]
    public async Task AddingIntoANestedFolderGrowsThatFolder()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        await explorer.OpenItemCommand.ExecuteAsync(explorer.Files!.Single(file => file.Name == "AdventOfCode"));
        var extra = Path.Combine(TempDirectory(), "notes2.txt");
        File.WriteAllBytes(extra, new byte[4]);
        PickDiskFiles(extra);

        await explorer.AddFilesCommand.ExecuteAsync(null);

        Assert.Contains(explorer.Files!, file => file.Name == "notes2");
        await explorer.NavigateUpCommand.ExecuteAsync(null);
        Assert.Equal("24 B", explorer.Files!.Single(file => file.Name == "AdventOfCode").Size);
    }
}
